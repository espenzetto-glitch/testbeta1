using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpEngine.Graphics3D.Scripting;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// A code editor in the browser for the local web server (Build Settings > WebGL > Run). The game page gets a "Code editor" button; the editor
/// lists, edits and creates the project's Scripts/*.cs files and can start the WebAssembly build again ("Save and Rebuild").
/// Only this computer can use it: every request needs the secret token of this server and a localhost Host header, and only .cs files
/// inside the Scripts folder can be written.
/// </summary>
public static partial class BuildPipeline
{
    /// <summary>Address of the browser code editor while the local server runs (it contains the secret token), otherwise null.</summary>
    public static string? WebEditorUrl { get; private set; }

    internal static void SetWebEditorUrl(string? url) => WebEditorUrl = url;

    private const int MaxScriptChars = 2 * 1024 * 1024;

    /// <summary>Answers /__editor and /__api/... requests. Returns false for every other request.</summary>
    internal static bool HandleWebEditor(HttpListenerContext ctx, int port, string token)
    {
        string path = ctx.Request.Url!.AbsolutePath;
        bool page = path == "/__editor";
        if (!page && !path.StartsWith("/__api/", StringComparison.Ordinal)) return false;
        try
        {
            string host = ctx.Request.Headers["Host"] ?? "";
            string given = page ? ctx.Request.QueryString["t"] ?? "" : ctx.Request.Headers["X-Editor-Token"] ?? "";
            if ((host != $"localhost:{port}" && host != $"127.0.0.1:{port}")
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(token)))
            {
                WebSend(ctx, 403, "text/plain", "Forbidden");
                return true;
            }
            if (page) { WebSend(ctx, 200, "text/html; charset=utf-8", WebEditorHtml.Replace("{{TOKEN}}", token)); return true; }

            string api = path.Substring("/__api/".Length);
            bool post = ctx.Request.HttpMethod == "POST";
            if (api == "files" && !post)
            {
                var names = ScriptManager.Files().Select(f => Path.GetRelativePath(ScriptManager.Directory, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                WebSend(ctx, 200, "application/json", JsonSerializer.Serialize(names));
            }
            else if (api == "file" && !post)
            {
                string? full = WebScriptPath(ctx.Request.QueryString["path"]);
                if (full is null || !File.Exists(full)) WebSend(ctx, 404, "text/plain", "No such script.");
                else WebSend(ctx, 200, "text/plain; charset=utf-8", File.ReadAllText(full));
            }
            else if (api == "file" && post)
            {
                string? full = WebScriptPath(ctx.Request.QueryString["path"]);
                if (full is null) { WebSend(ctx, 400, "text/plain", "Only .cs files inside the Scripts folder can be saved."); return true; }
                string text;
                using (var reader = new StreamReader(ctx.Request.InputStream, new UTF8Encoding(false)))
                {
                    var buffer = new char[MaxScriptChars + 1];
                    int n = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (n > MaxScriptChars) { WebSend(ctx, 413, "text/plain", "The script is too large."); return true; }
                    text = new string(buffer, 0, n);
                }
                ScriptManager.ProtectProject();
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
                WebSend(ctx, 200, "text/plain", "ok");
            }
            else if (api == "new" && post)
            {
                var clean = ScriptManager.SanitizeName(ctx.Request.QueryString["name"] ?? "");
                if (clean is null) { WebSend(ctx, 400, "text/plain", "Use letters, digits and underscores."); return true; }
                try
                {
                    string created = ScriptManager.CreateScript(clean);
                    WebSend(ctx, 200, "text/plain; charset=utf-8", Path.GetRelativePath(ScriptManager.Directory, created).Replace('\\', '/'));
                }
                catch (IOException ex) { WebSend(ctx, 409, "text/plain", ex.Message); }
            }
            else if (api == "build" && post)
            {
                string message; bool ok = false;
                if (_building) message = "A build is already running.";
                else if (SceneManager.Config.Platform != BuildPlatform.WebGL) message = "Set Platform to WebGL in the editor's Build Settings first.";
                else if (!Start(false)) message = Status.Length > 0 ? Status : "The build could not start.";
                else { ok = true; message = "Build started. The status appears below; reload the game when it is done."; }
                WebSend(ctx, 200, "application/json", JsonSerializer.Serialize(new { ok, message }));
            }
            else if (api == "status" && !post)
            {
                string[] lines;
                lock (Gate) lines = Log.ToString().Split('\n');
                var tail = lines.Skip(Math.Max(0, lines.Length - 120)).Select(l => l.TrimEnd('\r')).ToArray();
                WebSend(ctx, 200, "application/json", JsonSerializer.Serialize(new { building = _building, status = Status, error = StatusIsError, log = tail }));
            }
            else WebSend(ctx, 404, "text/plain", "Not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpListenerException or JsonException or ArgumentException)
        {
            try { WebSend(ctx, 500, "text/plain", ex.Message); } catch (Exception) { try { ctx.Response.Abort(); } catch (Exception) { } }
        }
        return true;
    }

    /// <summary>The full path of a script given as "Name.cs" or "World/Name.cs", or null when it is not a .cs file inside the Scripts folder.</summary>
    private static string? WebScriptPath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..") || relative.Contains(':') || Path.IsPathRooted(relative)
            || !relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;
        string root = Path.GetFullPath(ScriptManager.Directory) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static void WebSend(HttpListenerContext ctx, int status, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    /// <summary>Adds the "Code editor" button to the page the local server sends (the exported files stay untouched).</summary>
    internal static byte[] InjectEditorLink(byte[] html, string token)
    {
        string text = Encoding.UTF8.GetString(html);
        string link = $"<a href=\"/__editor?t={token}\" target=\"sharpeditor\" style=\"position:fixed;right:10px;bottom:10px;z-index:99999;padding:6px 12px;background:#2b6cb0;color:#fff;font:14px sans-serif;border-radius:6px;text-decoration:none;opacity:.85\">&lt;/&gt; Code editor</a>";
        int i = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return Encoding.UTF8.GetBytes(i >= 0 ? text.Insert(i, link) : text + link);
    }

    private const string WebEditorHtml = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>SharpEngine code editor</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
:root{--bg:#1e1f24;--panel:#26282e;--line:#14151a;--text:#dfe1e6;--dim:#8b909b;--acc:#2b6cb0}
*{box-sizing:border-box}
html,body{height:100%;margin:0;background:var(--bg);color:var(--text);font:14px system-ui,sans-serif}
#app{display:grid;grid-template-columns:230px 1fr;grid-template-rows:44px 1fr 170px;height:100%}
header{grid-column:1/3;display:flex;gap:8px;align-items:center;padding:6px 10px;background:var(--panel);border-bottom:1px solid var(--line)}
button{background:#3a3d46;color:var(--text);border:0;border-radius:4px;padding:6px 12px;cursor:pointer;font:inherit}
button:hover{background:#4a4e5a}
button.pri{background:var(--acc)}
#title{flex:1;color:var(--dim);overflow:hidden;white-space:nowrap;text-overflow:ellipsis}
aside{background:var(--panel);border-right:1px solid var(--line);overflow:auto;padding:8px}
aside .f{padding:4px 8px;border-radius:4px;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
aside .f:hover{background:#31343c}
aside .f.sel{background:var(--acc)}
#newrow{display:flex;gap:4px;margin-top:10px}
#newname{flex:1;min-width:0;background:#1b1c20;border:1px solid var(--line);color:var(--text);padding:5px;border-radius:4px}
main{position:relative;min-height:0}
#gut{position:absolute;left:0;top:0;bottom:0;width:52px;overflow:hidden;background:#222328;color:#6b7080;text-align:right;padding:8px 8px 8px 0;font:14px/20px Consolas,Menlo,monospace;white-space:pre;user-select:none}
#hlwrap{position:absolute;left:52px;right:0;top:0;bottom:0;overflow:hidden;pointer-events:none}
#hl,#ta{margin:0;padding:8px;font:14px/20px Consolas,Menlo,monospace;tab-size:4;white-space:pre;letter-spacing:0;border:0}
#hl{position:absolute;left:0;top:0;color:var(--text)}
#ta{position:absolute;left:52px;right:0;top:0;bottom:0;width:calc(100% - 52px);height:100%;resize:none;outline:none;background:transparent;color:transparent;caret-color:#fff;overflow:auto}
#ta::selection{background:rgba(52,100,170,.55);color:transparent}
footer{grid-column:1/3;background:#1a1b1f;border-top:1px solid var(--line);overflow:auto;padding:6px 10px;font:13px/18px Consolas,Menlo,monospace;white-space:pre-wrap}
footer .err{color:#f08080;cursor:pointer}
footer .ok{color:#78d782}
.k{color:#569cd6}.t{color:#4ec9b0}.s{color:#d69d85}.c{color:#6a9955}.n{color:#b5cea8}
</style></head>
<body>
<div id="app">
<header><strong>SharpEngine</strong><span id="title">Choose a script</span>
<button id="save" class="pri">Save (Ctrl+S)</button><button id="build">Save &amp; Rebuild</button><button id="open">Open game</button></header>
<aside><div id="files"></div><div id="newrow"><input id="newname" placeholder="New script name"><button id="add">Add</button></div></aside>
<main><div id="gut"></div><div id="hlwrap"><pre id="hl"></pre></div><textarea id="ta" spellcheck="false" autocapitalize="off" autocomplete="off"></textarea></main>
<footer id="log">Ready. Scripts are C# 15 (preview). Save &amp; Rebuild compiles them into the web game.</footer>
</div>
<script>
const TOKEN='{{TOKEN}}';
const $=id=>document.getElementById(id);
const ta=$('ta'),hlEl=$('hl'),gut=$('gut');
let files=[],cur=null,dirty=false,saved='',timer=null;
const KW=new Set('abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var union closed record required partial with and or not async await yield get set init extension scoped'.split(' '));
function esc(s){return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');}
function hl(src){
  const re=/(\/\/[^\n]*|\/\*[\s\S]*?(?:\*\/|$))|("(?:[^"\\\n]|\\.)*"?|'(?:[^'\\\n]|\\.)*'?|@"(?:[^"]|"")*"?)|(\b\d[\d_]*\.?\d*[fFdDmMuUlL]*\b)|(\b[A-Za-z_]\w*\b)/g;
  let out='',last=0,m;
  while((m=re.exec(src))){
    out+=esc(src.slice(last,m.index));
    const t=m[0];
    if(m[1])out+='<span class="c">'+esc(t)+'</span>';
    else if(m[2])out+='<span class="s">'+esc(t)+'</span>';
    else if(m[3])out+='<span class="n">'+esc(t)+'</span>';
    else if(KW.has(t))out+='<span class="k">'+esc(t)+'</span>';
    else if(/^[A-Z]/.test(t))out+='<span class="t">'+esc(t)+'</span>';
    else out+=esc(t);
    last=m.index+t.length;
  }
  return out+esc(src.slice(last));
}
function title(){$('title').textContent=(cur||'Choose a script')+(dirty?' *':'');}
function render(){
  hlEl.innerHTML=hl(ta.value)+'\n';
  const n=ta.value.split('\n').length;let g='';
  for(let i=1;i<=n;i++)g+=i+'\n';
  gut.textContent=g;
  dirty=cur!==null&&ta.value!==saved;
  title();
}
function syncScroll(){hlEl.style.transform='translate('+(-ta.scrollLeft)+'px,'+(-ta.scrollTop)+'px)';gut.scrollTop=ta.scrollTop;}
async function api(path,opt){
  opt=opt||{};
  const r=await fetch('/__api/'+path,Object.assign({},opt,{headers:Object.assign({'X-Editor-Token':TOKEN},opt.headers||{})}));
  if(!r.ok)throw new Error((await r.text())||String(r.status));
  return r;
}
function say(msg,cls){const b=$('log');b.innerHTML='';const d=document.createElement('div');d.className=cls||'';d.textContent=msg;b.appendChild(d);}
async function loadFiles(){
  files=await(await api('files')).json();
  const box=$('files');box.innerHTML='';
  for(const f of files){
    const d=document.createElement('div');
    d.className='f'+(f===cur?' sel':'');d.textContent=f;d.title=f;
    d.onclick=()=>openFile(f);
    box.appendChild(d);
  }
}
function goTo(line){
  const lines=ta.value.split('\n');let pos=0;
  for(let i=0;i<line-1&&i<lines.length;i++)pos+=lines[i].length+1;
  ta.focus();ta.setSelectionRange(pos,pos+(lines[line-1]||'').length);
  ta.scrollTop=Math.max(0,(line-5)*20);syncScroll();
}
async function openFile(f,line){
  if(dirty&&f!==cur&&!confirm('Discard unsaved changes in '+cur+'?'))return;
  try{
    const t=await(await api('file?path='+encodeURIComponent(f))).text();
    cur=f;saved=t;ta.value=t;render();syncScroll();loadFiles();
    if(line)goTo(line);else ta.focus();
  }catch(e){say('Could not open '+f+': '+e.message,'err');}
}
async function save(){
  if(cur===null)return true;
  try{
    await api('file?path='+encodeURIComponent(cur),{method:'POST',body:ta.value});
    saved=ta.value;render();say('Saved '+cur,'ok');return true;
  }catch(e){say('Save failed: '+e.message,'err');return false;}
}
function showStatus(j){
  const box=$('log');box.innerHTML='';
  const h=document.createElement('div');h.className=j.error?'err':'ok';
  h.textContent=(j.building?'Building... ':'')+j.status;box.appendChild(h);
  for(const l of j.log){
    const d=document.createElement('div');d.textContent=l;
    if(/: error /.test(l)){d.className='err';d.onclick=()=>jumpTo(l);}
    box.appendChild(d);
  }
  box.scrollTop=box.scrollHeight;
}
function jumpTo(l){
  const m=/([^\s()"]+\.cs)\((\d+),(\d+)\)/.exec(l);if(!m)return;
  const name=m[1].replace(/\\/g,'/').split('/').pop();
  const f=files.find(x=>x.split('/').pop()===name);if(!f)return;
  if(f===cur)goTo(+m[2]);else openFile(f,+m[2]);
}
function poll(){
  if(timer)return;
  const tick=async()=>{
    try{const j=await(await api('status')).json();showStatus(j);if(!j.building){timer=null;return;}}catch(e){}
    timer=setTimeout(tick,1500);
  };
  timer=setTimeout(tick,500);
}
async function build(){
  if(!(await save()))return;
  try{
    const j=await(await api('build',{method:'POST'})).json();
    say(j.message,j.ok?'ok':'err');
    if(j.ok)poll();
  }catch(e){say(e.message,'err');}
}
function toggleComment(){
  const v=ta.value;
  const s=v.lastIndexOf('\n',ta.selectionStart-1)+1;
  let e=v.indexOf('\n',ta.selectionEnd);if(e<0)e=v.length;
  const seg=v.slice(s,e).split('\n');
  const all=seg.every(l=>/^\s*\/\//.test(l)||l.trim()==='');
  const out=seg.map(l=>all?l.replace(/^(\s*)\/\/ ?/,'$1'):(l.trim()===''?l:l.replace(/^(\s*)/,'$1// '))).join('\n');
  ta.setRangeText(out,s,e,'select');render();
}
ta.addEventListener('input',render);
ta.addEventListener('scroll',syncScroll);
ta.addEventListener('keydown',e=>{
  if((e.ctrlKey||e.metaKey)&&e.key==='s'){e.preventDefault();save();return;}
  if((e.ctrlKey||e.metaKey)&&e.key==='/'){e.preventDefault();toggleComment();return;}
  if(e.key==='Tab'){e.preventDefault();if(!e.shiftKey)document.execCommand('insertText',false,'    ');return;}
  if(e.key==='Enter'&&!e.ctrlKey){
    const before=ta.value.slice(0,ta.selectionStart);
    const line=before.slice(before.lastIndexOf('\n')+1);
    let ind=/^\s*/.exec(line)[0];
    if(/\{\s*$/.test(line))ind+='    ';
    e.preventDefault();document.execCommand('insertText',false,'\n'+ind);
  }
});
window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
$('save').onclick=save;
$('build').onclick=build;
$('open').onclick=()=>window.open('/','sharpgame');
$('add').onclick=async()=>{
  const n=$('newname').value.trim();if(!n)return;
  try{
    const r=await(await api('new?name='+encodeURIComponent(n),{method:'POST'})).text();
    $('newname').value='';await loadFiles();openFile(r);
  }catch(e){say('Could not create the script: '+e.message,'err');}
};
loadFiles().then(()=>{if(files.length)openFile(files[0]);}).catch(e=>say('Could not load the scripts: '+e.message,'err'));
</script>
</body></html>

""";
}