#!/usr/bin/env python3
"""
folder_to_github.py – Turn any folder into a GitHub repository easily.
Default folder: E:\AiMMorpg
"""

import argparse
import json
import os
import re
import secrets
import shutil
import subprocess
import sys
import threading
import urllib.error
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

INFO_FILE = "project.info.json"
IGNORED_DIRS = {".git", "node_modules", "__pycache__", ".venv", "venv", ".idea", ".vscode"}
MAX_EDIT_BYTES = 2 * 1024 * 1024

DEFAULT_GITIGNORE = """\
# OS / editor
.DS_Store
Thumbs.db
.idea/
.vscode/

# Secrets
.env
*.pem

# Python
__pycache__/
*.pyc
.venv/
venv/

# Node
node_modules/

# Build
dist/
build/
*.log
"""


class PublishError(Exception):
    """Friendly error shown to the user."""


def run(cmd, cwd, check=True, secret=None):
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    if check and result.returncode != 0:
        msg = (result.stderr or result.stdout).strip()
        if secret:
            msg = msg.replace(secret, "***")
        raise PublishError(f"`{' '.join(cmd[:2])}` failed:\n{msg}")
    return result


def gh_cli_ready():
    return shutil.which("gh") and subprocess.run(
        ["gh", "auth", "status"], capture_output=True
    ).returncode == 0


def create_repo_via_api(token, name, private, description):
    data = json.dumps({
        "name": name,
        "private": private,
        "description": description
    }).encode()

    req = urllib.request.Request(
        "https://api.github.com/user/repos",
        data=data,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
            "User-Agent": "folder-to-github",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req) as resp:
            return json.load(resp)
    except urllib.error.HTTPError as e:
        body = e.read().decode(errors="replace")
        if e.code == 422:
            raise PublishError(f"Repo '{name}' may already exist.\n{body}")
        if e.code == 401:
            raise PublishError("Invalid GitHub token. Check GITHUB_TOKEN or the token you entered.")
        raise PublishError(f"GitHub API error {e.code}:\n{body}")
    except urllib.error.URLError as e:
        raise PublishError(f"Could not reach GitHub: {e.reason}")


def strip_credentials(url):
    return re.sub(r"//[^/@]+@", "//", url or "")


def publish(folder, opts, log):
    folder = Path(folder)
    if not shutil.which("git"):
        raise PublishError("git is not installed.\nDownload: https://git-scm.com")

    name = (opts.get("name") or folder.name).strip().replace(" ", "-")
    private = bool(opts.get("private"))
    branch = (opts.get("branch") or "main").strip()
    message = (opts.get("message") or "Update").strip()
    description = opts.get("description") or ""
    token = opts.get("token") or os.environ.get("GITHUB_TOKEN")
    use_cli = gh_cli_ready()

    if not use_cli and not token:
        raise PublishError(
            "No GitHub authentication found.\n\n"
            "Do one of these:\n"
            "  1. Run:  gh auth login\n"
            "  2. Set:  set GITHUB_TOKEN=your_token\n"
            "  3. Paste a token in the UI"
        )

    if not (folder / ".git").exists():
        log("Initializing git repository…")
        run(["git", "init", "-b", branch], folder)
    else:
        log("Using existing git repository.")
        run(["git", "checkout", "-B", branch], folder, check=False)

    gitignore = folder / ".gitignore"
    if not gitignore.exists():
        gitignore.write_text(DEFAULT_GITIGNORE)
        log("Created starter .gitignore")

    run(["git", "add", "-A"], folder)
    if run(["git", "status", "--porcelain"], folder).stdout.strip():
        run(["git", "commit", "-m", message], folder)
        log(f"Committed: {message}")
    else:
        log("Nothing new to commit.")

    origin = run(["git", "remote", "get-url", "origin"], folder, check=False)
    if origin.returncode == 0:
        origin_url = origin.stdout.strip()
        log("Remote already exists → pushing…")
        if use_cli or not origin_url.startswith("https://"):
            run(["git", "push", "-u", "origin", branch], folder)
        else:
            auth = strip_credentials(origin_url).replace(
                "https://", f"https://x-access-token:{token}@"
            )
            run(["git", "push", "-u", auth, branch], folder, secret=token)
        return strip_credentials(origin_url).removesuffix(".git")

    visibility = "--private" if private else "--public"
    if use_cli:
        log(f"Creating GitHub repo '{name}' with GitHub CLI…")
        cmd = ["gh", "repo", "create", name, visibility,
               "--source", ".", "--remote", "origin", "--push"]
        if description:
            cmd += ["--description", description]
        run(cmd, folder)
        return run(["gh", "repo", "view", "--json", "url", "-q", ".url"], folder).stdout.strip()

    log(f"Creating GitHub repo '{name}' via API…")
    repo = create_repo_via_api(token, name, private, description)
    clean_url = repo["clone_url"]
    run(["git", "remote", "add", "origin", clean_url], folder)
    log("Pushing…")
    auth = clean_url.replace("https://", f"https://x-access-token:{token}@")
    run(["git", "push", "-u", auth, branch], folder, secret=token)
    run(["git", "branch", "--set-upstream-to", f"origin/{branch}", branch], folder, check=False)
    return repo["html_url"]


def default_info(folder):
    return {
        "name": Path(folder).name.replace(" ", "-"),
        "description": "",
        "private": False,
        "branch": "main",
        "message": "Initial commit",
    }


def load_info(folder):
    info = default_info(folder)
    path = Path(folder) / INFO_FILE
    if path.exists():
        try:
            saved = json.loads(path.read_text(encoding="utf-8"))
            info.update({k: saved[k] for k in info if k in saved})
        except (OSError, ValueError):
            pass
    return info


def save_info(folder, data):
    info = default_info(folder)
    for k in info:
        if k in data:
            info[k] = data[k]
    info["private"] = bool(info["private"])
    (Path(folder) / INFO_FILE).write_text(
        json.dumps(info, indent=2) + "\n", encoding="utf-8"
    )
    return info


def safe_path(root, rel):
    p = (root / (rel or "")).resolve()
    if p != root and root not in p.parents:
        raise ValueError("Path escapes the project folder.")
    if ".git" in p.relative_to(root).parts:
        raise ValueError(".git is protected.")
    return p


def build_tree(base, root, counter):
    items = []
    for e in sorted(base.iterdir(), key=lambda p: (p.is_file(), p.name.lower())):
        if e.name in IGNORED_DIRS or counter[0] > 5000:
            continue
        counter[0] += 1
        rel = e.relative_to(root).as_posix()
        if e.is_dir() and not e.is_symlink():
            items.append({
                "name": e.name,
                "path": rel,
                "type": "dir",
                "children": build_tree(e, root, counter)
            })
        elif e.is_file():
            items.append({"name": e.name, "path": rel, "type": "file"})
    return items


def git_status(root):
    out = {
        "auth": {
            "gh": gh_cli_ready(),
            "token": bool(os.environ.get("GITHUB_TOKEN"))
        },
        "repo": (root / ".git").exists(),
        "changes": [],
        "remote": None
    }
    if out["repo"]:
        out["changes"] = run(
            ["git", "status", "--porcelain"], root, check=False
        ).stdout.splitlines()[:300]
        r = run(["git", "remote", "get-url", "origin"], root, check=False)
        if r.returncode == 0:
            out["remote"] = strip_credentials(r.stdout.strip())
    return out


class Handler(BaseHTTPRequestHandler):
    root = None
    token = None
    port = None
    job = {"log": [], "done": True, "url": None, "error": None}
    job_lock = threading.Lock()

    def log_message(self, *args):
        pass

    def _send(self, code, body, ctype="application/json"):
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _host_ok(self):
        return self.headers.get("Host", "") in (
            f"127.0.0.1:{self.port}", f"localhost:{self.port}"
        )

    def _authorized(self):
        return self._host_ok() and secrets.compare_digest(
            self.headers.get("X-Admin-Token", ""), self.token
        )

    def _body(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length > 10 * 1024 * 1024:
            raise ValueError("Request too large.")
        data = self.rfile.read(length)
        return json.loads(data or b"{}")

    def _dispatch(self, method):
        url = urlparse(self.path)

        if method == "GET" and url.path == "/":
            if not self._host_ok():
                return self._send(403, {"error": "Bad host"})
            page = INDEX_HTML.replace("__TOKEN__", self.token)
            return self._send(200, page.encode(), "text/html; charset=utf-8")

        if not url.path.startswith("/api/"):
            return self._send(404, {"error": "Not found"})
        if not self._authorized():
            return self._send(403, {"error": "Forbidden"})

        try:
            endpoint = url.path[5:].replace("/", "_")
            handler = getattr(self, f"api_{method.lower()}_{endpoint}", None)
            if handler is None:
                return self._send(404, {"error": "Unknown endpoint"})
            body = self._body() if method == "POST" else None
            code, payload = handler(parse_qs(url.query), body)
            self._send(code, payload)
        except (PublishError, ValueError, OSError) as e:
            self._send(400, {"error": str(e)})
        except Exception as e:
            self._send(500, {"error": f"Unexpected error: {e}"})

    def do_GET(self):
        self._dispatch("GET")

    def do_POST(self):
        self._dispatch("POST")

    def api_get_project(self, q, b):
        return 200, {
            "open": self.root is not None,
            "name": self.root.name if self.root else None,
            "path": str(self.root) if self.root else None,
        }

    def api_get_tree(self, q, b):
        return 200, {
            "name": self.root.name,
            "tree": build_tree(self.root, self.root, [0])
        }

    def api_get_file(self, q, b):
        p = safe_path(self.root, q.get("path", [""])[0])
        if not p.is_file():
            raise ValueError("Not a file.")
        if p.stat().st_size > MAX_EDIT_BYTES:
            raise ValueError("File is too large to edit here (max 2 MB).")
        raw = p.read_bytes()
        if b"\x00" in raw:
            raise ValueError("Binary file – cannot edit as text.")
        return 200, {"content": raw.decode("utf-8", errors="replace")}

    def api_post_file(self, q, b):
        p = safe_path(self.root, b.get("path", ""))
        if p == self.root or p.is_dir():
            raise ValueError("Not a file.")
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(b.get("content", ""), encoding="utf-8", newline="")
        return 200, {"ok": True}

    def api_post_create(self, q, b):
        p = safe_path(self.root, b.get("path", ""))
        if p == self.root:
            raise ValueError("Give the new item a name.")
        if p.exists():
            raise ValueError("That already exists.")
        if b.get("type") == "dir":
            p.mkdir(parents=True)
        else:
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("", encoding="utf-8")
        return 200, {"ok": True}

    def api_post_rename(self, q, b):
        src = safe_path(self.root, b.get("path", ""))
        dst = safe_path(self.root, b.get("new_path", ""))
        if src == self.root or dst == self.root:
            raise ValueError("Can't rename the project root.")
        if not src.exists():
            raise ValueError("Source doesn't exist.")
        if dst.exists():
            raise ValueError("Destination already exists.")
        dst.parent.mkdir(parents=True, exist_ok=True)
        src.rename(dst)
        return 200, {"ok": True}

    def api_post_delete(self, q, b):
        p = safe_path(self.root, b.get("path", ""))
        if p == self.root:
            raise ValueError("Can't delete the project root.")
        if p.is_dir() and not p.is_symlink():
            shutil.rmtree(p)
        elif p.exists() or p.is_symlink():
            p.unlink()
        else:
            raise ValueError("Doesn't exist.")
        return 200, {"ok": True}

    def api_get_info(self, q, b):
        return 200, load_info(self.root)

    def api_post_info(self, q, b):
        return 200, save_info(self.root, b)

    def api_post_readme(self, q, b):
        p = self.root / "README.md"
        if p.exists():
            raise ValueError("README.md already exists – edit it instead.")
        info = load_info(self.root)
        p.write_text(
            f"# {info['name']}\n\n{info['description']}\n".rstrip() + "\n",
            encoding="utf-8"
        )
        return 200, {"ok": True}

    def api_get_status(self, q, b):
        return 200, git_status(self.root)

    def api_post_publish(self, q, b):
        with Handler.job_lock:
            if not Handler.job["done"]:
                raise ValueError("A publish is already running.")
            job = Handler.job = {
                "log": [], "done": False, "url": None, "error": None
            }

        opts = {k: b.get(k) for k in (
            "name", "description", "private", "branch", "message", "token"
        )}

        def work():
            try:
                job["url"] = publish(self.root, opts, job["log"].append)
            except PublishError as e:
                job["error"] = str(e)
            except Exception as e:
                job["error"] = f"Unexpected error: {e}"
            finally:
                job["done"] = True

        threading.Thread(target=work, daemon=True).start()
        return 200, {"started": True}

    def api_get_job(self, q, b):
        job = Handler.job
        since = int(q.get("since", ["0"])[0])
        return 200, {
            "lines": job["log"][since:],
            "next": len(job["log"]),
            "done": job["done"],
            "url": job["url"],
            "error": job["error"]
        }


def serve(folder, args):
    Handler.root = folder
    Handler.token = secrets.token_urlsafe(24)
    try:
        srv = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    except OSError:
        srv = ThreadingHTTPServer(("127.0.0.1", 0), Handler)

    Handler.port = srv.server_address[1]
    url = f"http://127.0.0.1:{Handler.port}/"

    print(f"Editor for: {folder}")
    print(f"Open → {url}")
    print("Press Ctrl+C to stop.")

    if not args.no_browser:
        webbrowser.open(url)

    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        srv.server_close()


INDEX_HTML = r'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Folder → GitHub</title>
<style>
:root {
  --bg: #0f1115; --panel: #171a21; --line: #2a2f3a;
  --fg: #e6e8ee; --mute: #8b93a7; --acc: #4f8cff;
  --ok: #3ecf8e; --bad: #ff6b6b;
}
@media (prefers-color-scheme: light) {
  :root {
    --bg: #f6f7fb; --panel: #fff; --line: #dfe3ec;
    --fg: #1b1f2a; --mute: #6a7388; --acc: #2f6df6;
    --ok: #13a463; --bad: #d93f3f;
  }
}
* { box-sizing: border-box; }
body {
  margin: 0; background: var(--bg); color: var(--fg);
  font: 14px/1.45 system-ui, sans-serif;
  height: 100vh; display: flex; flex-direction: column;
}
header {
  display: flex; align-items: center; gap: 12px;
  padding: 10px 16px; background: var(--panel);
  border-bottom: 1px solid var(--line);
}
header h1 { font-size: 15px; margin: 0; font-weight: 600; }
.tabs { display: flex; gap: 4px; margin-left: auto; }
button, input, select, textarea { font: inherit; color: var(--fg); }
button {
  background: var(--panel); border: 1px solid var(--line);
  border-radius: 6px; padding: 5px 10px; cursor: pointer;
}
button:hover { border-color: var(--acc); }
button.primary { background: var(--acc); border-color: var(--acc); color: #fff; }
button.danger:hover { border-color: var(--bad); color: var(--bad); }
.tab.active { background: var(--acc); border-color: var(--acc); color: #fff; }
main {
  flex: 1; display: grid; grid-template-columns: 280px 1fr; min-height: 0;
}
aside {
  border-right: 1px solid var(--line); background: var(--panel);
  display: flex; flex-direction: column; min-height: 0;
}
.toolbar {
  display: flex; flex-wrap: wrap; gap: 6px;
  padding: 8px; border-bottom: 1px solid var(--line);
}
.toolbar button { padding: 3px 8px; font-size: 12px; }
#tree { flex: 1; overflow: auto; padding: 6px 8px; }
#tree ul { list-style: none; margin: 0; padding-left: 14px; }
#tree > ul { padding-left: 0; }
#tree summary, #tree .file {
  cursor: pointer; padding: 2px 6px; border-radius: 4px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
#tree summary:hover, #tree .file:hover { background: var(--bg); }
#tree .sel { background: var(--bg); outline: 1px solid var(--acc); }
section {
  display: none; min-width: 0; min-height: 0; flex-direction: column;
}
section.active { display: flex; }
.bar {
  display: flex; align-items: center; gap: 10px;
  padding: 8px 12px; border-bottom: 1px solid var(--line);
}
.bar .path {
  font-family: ui-monospace, monospace; color: var(--mute);
  flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
#editor {
  flex: 1; width: 100%; border: 0; outline: 0; resize: none;
  background: var(--bg); padding: 14px;
  font: 13px/1.55 ui-monospace, SFMono-Regular, Menlo, monospace;
  tab-size: 2; white-space: pre;
}
#empty { flex: 1; display: grid; place-items: center; color: var(--mute); }
.panel { padding: 20px; overflow: auto; max-width: 820px; }
.panel h2 { margin: 0 0 12px; font-size: 16px; }
.grid {
  display: grid; grid-template-columns: 150px 1fr;
  gap: 10px 12px; align-items: center; margin-bottom: 14px;
}
.grid input, .grid select {
  background: var(--panel); border: 1px solid var(--line);
  border-radius: 6px; padding: 6px 8px; width: 100%;
}
.hint { color: var(--mute); font-size: 12px; }
.row { display: flex; gap: 8px; flex-wrap: wrap; margin: 12px 0; }
pre {
  background: var(--panel); border: 1px solid var(--line);
  border-radius: 8px; padding: 10px; max-height: 240px;
  overflow: auto; margin: 8px 0; font-size: 12px; white-space: pre-wrap;
}
.pill {
  display: inline-block; padding: 1px 8px; border-radius: 999px;
  font-size: 12px; border: 1px solid var(--line); margin-right: 6px;
}
.ok { color: var(--ok); } .bad { color: var(--bad); }
#toast {
  position: fixed; right: 16px; bottom: 16px;
  background: var(--panel); border: 1px solid var(--line);
  padding: 8px 14px; border-radius: 8px;
  opacity: 0; transition: .2s; pointer-events: none;
}
#toast.show { opacity: 1; }
@media (max-width: 700px) {
  main { grid-template-columns: 1fr; }
  aside { max-height: 35vh; }
}
</style>
</head>
<body>

<header>
  <h1>📁 <span id="projName"></span></h1>
  <span class="hint" id="dirtyMark"></span>
  <div class="tabs">
    <button class="tab active" data-view="edit">Editor</button>
    <button class="tab" data-view="pub">Publish</button>
  </div>
</header>

<main>
  <aside>
    <div class="toolbar">
      <button id="bNewFile">+ File</button>
      <button id="bNewDir">+ Folder</button>
      <button id="bRename">Rename</button>
      <button id="bDelete" class="danger">Delete</button>
      <button id="bRefresh">↻</button>
    </div>
    <div id="tree"></div>
  </aside>

  <section id="v-edit" class="active">
    <div class="bar">
      <span class="path" id="curPath">No file open</span>
      <button id="bSave" class="primary">Save (Ctrl+S)</button>
    </div>
    <div id="empty">Select a file in the tree to edit it.</div>
    <textarea id="editor" spellcheck="false" style="display:none"></textarea>
  </section>

  <section id="v-pub">
    <div class="panel">
      <h2>Project settings</h2>
      <div class="grid">
        <label for="fName">Repo name</label>
        <input id="fName">
        <label for="fDesc">Description</label>
        <input id="fDesc">
        <label for="fVis">Visibility</label>
        <select id="fVis">
          <option value="public">Public</option>
          <option value="private">Private</option>
        </select>
        <label for="fBranch">Branch</label>
        <input id="fBranch">
        <label for="fMsg">Commit message</label>
        <input id="fMsg">
        <label for="fToken">Token (optional)</label>
        <input id="fToken" type="password"
               placeholder="Only if gh / GITHUB_TOKEN not set" autocomplete="off">
      </div>
      <div class="hint">Settings are saved to <code>project.info.json</code>. Token is never saved.</div>
      <div class="row">
        <button id="bInfo">Save settings</button>
        <button id="bReadme">Create README.md</button>
        <button id="bPublish" class="primary">Publish to GitHub</button>
      </div>
      <div id="pubResult"></div>
      <pre id="log" hidden></pre>

      <h2>Git status</h2>
      <div id="auth"></div>
      <div class="hint" id="remote"></div>
      <pre id="changes">Loading…</pre>
    </div>
  </section>
</main>

<div id="toast"></div>

<script>
const TOKEN = "__TOKEN__";
const $ = id => document.getElementById(id);
let selected = {path: "", type: "dir"};
let current = null;
let dirty = false;

async function api(path, opts = {}) {
  const r = await fetch(path, {
    ...opts,
    headers: {
      "X-Admin-Token": TOKEN,
      "Content-Type": "application/json"
    }
  });
  let j = {};
  try { j = await r.json(); } catch (e) {}
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
}
const post = (p, b) => api(p, {method: "POST", body: JSON.stringify(b || {})});

function toast(msg, bad) {
  const t = $("toast");
  t.textContent = msg;
  t.style.borderColor = bad ? "var(--bad)" : "var(--line)";
  t.classList.add("show");
  setTimeout(() => t.classList.remove("show"), 2600);
}

async function guard(fn) {
  try { return await fn(); }
  catch (e) { toast(e.message, true); }
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

document.querySelectorAll(".tab").forEach(b => b.onclick = () => {
  document.querySelectorAll(".tab").forEach(x => x.classList.toggle("active", x === b));
  $("v-edit").classList.toggle("active", b.dataset.view === "edit");
  $("v-pub").classList.toggle("active", b.dataset.view === "pub");
  if (b.dataset.view === "pub") refreshStatus();
});

function renderTree(items) {
  const ul = document.createElement("ul");
  for (const it of items) {
    const li = document.createElement("li");
    if (it.type === "dir") {
      const d = document.createElement("details");
      d.open = it.path === selected.path ||
               selected.path.startsWith(it.path + "/") ||
               !window._seen;
      const s = document.createElement("summary");
      s.textContent = "📁 " + it.name;
      s.dataset.path = it.path;
      s.onclick = () => { selected = {path: it.path, type: "dir"}; markSel(); };
      d.append(s, renderTree(it.children));
      li.append(d);
    } else {
      const f = document.createElement("div");
      f.className = "file";
      f.textContent = "📄 " + it.name;
      f.dataset.path = it.path;
      f.onclick = () => openFile(it.path);
      li.append(f);
    }
    ul.append(li);
  }
  return ul;
}

function markSel() {
  document.querySelectorAll("#tree [data-path]").forEach(e =>
    e.classList.toggle("sel", e.dataset.path === selected.path && selected.path !== "")
  );
}

async function loadTree() {
  const t = await api("/api/tree");
  $("projName").textContent = t.name;
  $("tree").replaceChildren(renderTree(t.tree));
  window._seen = true;
  markSel();
}

function setDirty(v) {
  dirty = v;
  $("dirtyMark").textContent = v ? "● unsaved" : "";
}

async function openFile(path) {
  if (dirty && !confirm("Discard unsaved changes?")) return;
  await guard(async () => {
    const r = await api("/api/file?path=" + encodeURIComponent(path));
    current = path;
    selected = {path, type: "file"};
    markSel();
    $("curPath").textContent = path;
    $("editor").value = r.content;
    $("editor").style.display = "block";
    $("empty").style.display = "none";
    setDirty(false);
    document.querySelector('.tab[data-view="edit"]').click();
  });
}

$("editor").oninput = () => setDirty(true);
$("editor").onkeydown = e => {
  if (e.key === "Tab") {
    e.preventDefault();
    const t = e.target, s = t.selectionStart;
    t.setRangeText("  ", s, t.selectionEnd, "end");
    setDirty(true);
  }
};

async function saveFile() {
  if (!current) return;
  await guard(async () => {
    await post("/api/file", {path: current, content: $("editor").value});
    setDirty(false);
    toast("Saved " + current);
  });
}
$("bSave").onclick = saveFile;
addEventListener("keydown", e => {
  if ((e.ctrlKey || e.metaKey) && e.key === "s") {
    e.preventDefault();
    saveFile();
  }
});
addEventListener("beforeunload", e => {
  if (dirty) { e.preventDefault(); e.returnValue = ""; }
});

const baseDir = () => selected.type === "dir"
  ? selected.path
  : selected.path.split("/").slice(0, -1).join("/");
const join = (a, b) => a ? a + "/" + b : b;

async function create(type) {
  const name = prompt(type === "dir" ? "New folder name:" : "New file name:");
  if (!name) return;
  await guard(async () => {
    const p = join(baseDir(), name.trim().replace(/^\/+/, ""));
    await post("/api/create", {path: p, type});
    await loadTree();
    if (type === "file") openFile(p);
  });
}
$("bNewFile").onclick = () => create("file");
$("bNewDir").onclick = () => create("dir");

$("bRename").onclick = async () => {
  if (!selected.path) return toast("Select something first", true);
  const parts = selected.path.split("/");
  const old = parts.pop();
  const nn = prompt("Rename to:", old);
  if (!nn || nn === old) return;
  await guard(async () => {
    const np = join(parts.join("/"), nn.trim());
    await post("/api/rename", {path: selected.path, new_path: np});
    if (current === selected.path) {
      current = np;
      $("curPath").textContent = np;
    }
    selected.path = np;
    await loadTree();
  });
};

$("bDelete").onclick = async () => {
  if (!selected.path) return toast("Select something first", true);
  if (!confirm("Delete " + selected.path + "?\nThis cannot be undone.")) return;
  await guard(async () => {
    await post("/api/delete", {path: selected.path});
    if (current && (current === selected.path || current.startsWith(selected.path + "/"))) {
      current = null;
      setDirty(false);
      $("editor").style.display = "none";
      $("empty").style.display = "grid";
      $("curPath").textContent = "No file open";
    }
    selected = {path: "", type: "dir"};
    await loadTree();
  });
};
$("bRefresh").onclick = () => guard(loadTree);

function formData() {
  return {
    name: $("fName").value.trim(),
    description: $("fDesc").value,
    private: $("fVis").value === "private",
    branch: $("fBranch").value.trim() || "main",
    message: $("fMsg").value.trim() || "Update"
  };
}

async function loadInfo() {
  const i = await api("/api/info");
  $("fName").value = i.name;
  $("fDesc").value = i.description;
  $("fVis").value = i.private ? "private" : "public";
  $("fBranch").value = i.branch;
  $("fMsg").value = i.message;
}

$("bInfo").onclick = () => guard(async () => {
  await post("/api/info", formData());
  toast("Settings saved");
  loadTree();
});

$("bReadme").onclick = () => guard(async () => {
  await post("/api/info", formData());
  await post("/api/readme");
  toast("README.md created");
  loadTree();
});

async function refreshStatus() {
  await guard(async () => {
    const s = await api("/api/status");
    const a = $("auth");
    a.replaceChildren();
    for (const [label, ok] of [["GitHub CLI", s.auth.gh], ["GITHUB_TOKEN", s.auth.token]]) {
      const p = document.createElement("span");
      p.className = "pill " + (ok ? "ok" : "bad");
      p.textContent = label + ": " + (ok ? "ready" : "not found");
      a.append(p);
    }
    $("remote").textContent = s.repo
      ? (s.remote ? "Remote: " + s.remote : "Git repo, no remote yet.")
      : "Not a git repo yet. Publishing will create one.";
    $("changes").textContent = s.changes.length
      ? s.changes.join("\n")
      : (s.repo ? "Working tree clean." : "(nothing tracked yet)");
  });
}

$("bPublish").onclick = async () => {
  if (dirty && !confirm("You have unsaved edits that won't be included. Publish anyway?")) return;
  const log = $("log"), res = $("pubResult");
  log.hidden = false;
  log.textContent = "";
  res.textContent = "";
  $("bPublish").disabled = true;

  try {
    const data = formData();
    await post("/api/info", data);
    await post("/api/publish", {...data, token: $("fToken").value.trim() || undefined});

    let since = 0;
    for (;;) {
      const j = await api("/api/job?since=" + since);
      since = j.next;
      if (j.lines.length) {
        log.textContent += j.lines.join("\n") + "\n";
        log.scrollTop = log.scrollHeight;
      }
      if (j.done) {
        if (j.error) {
          res.className = "bad";
          res.textContent = "Failed: " + j.error;
        } else {
          res.className = "ok";
          res.textContent = "Published → ";
          const a = document.createElement("a");
          a.href = j.url;
          a.target = "_blank";
          a.rel = "noopener";
          a.textContent = j.url;
          res.append(a);
        }
        break;
      }
      await sleep(600);
    }
  } catch (e) {
    res.className = "bad";
    res.textContent = "Failed: " + e.message;
  }
  $("bPublish").disabled = false;
  refreshStatus();
  loadTree();
};

guard(async () => {
  await loadTree();
  await loadInfo();
  await refreshStatus();
});
</script>
</body>
</html>
'''


def main():
    p = argparse.ArgumentParser(
        description="Turn any folder into a GitHub repository (with optional editor UI)."
    )
    p.add_argument("folder", nargs="?", default=r"E:\AiMMorpg",
                   help="Folder to use (default: E:\\AiMMorpg)")
    mode = p.add_mutually_exclusive_group()
    mode.add_argument("--ui", action="store_true",
                      help="Open the editor UI (default)")
    mode.add_argument("--publish", action="store_true",
                      help="Publish from the command line (no UI)")
    p.add_argument("--port", type=int, default=8765, help="UI port (default 8765)")
    p.add_argument("--no-browser", action="store_true", help="Don't open browser")
    p.add_argument("--name", help="Repository name")
    p.add_argument("--description", help="Repository description")
    p.add_argument("--private", action="store_true", help="Make the repo private")
    p.add_argument("--branch", help="Branch name (default: main)")
    p.add_argument("--message", help="Commit message")

    args, unknown = p.parse_known_args()
    if unknown:
        print("Ignoring unrecognized arguments:", " ".join(unknown), file=sys.stderr)

    folder = Path(args.folder).expanduser().resolve()
    if not folder.is_dir():
        sys.exit(f"Not a folder: {folder}")

    if not args.publish:
        serve(folder, args)
        return

    opts = load_info(folder)
    for key in ("name", "description", "branch", "message"):
        if getattr(args, key):
            opts[key] = getattr(args, key)
    if args.private:
        opts["private"] = True

    try:
        url = publish(folder, opts, print)
    except PublishError as e:
        sys.exit(str(e))

    print(f"\nDone! Your folder is now on GitHub:\n{url}")


if __name__ == "__main__":
    main()