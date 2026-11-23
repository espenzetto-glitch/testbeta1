namespace SharpEngine.Graphics3D.Editor;

/// <summary>The browser player shipped with every WebGL build (plain WebGL 1 + JavaScript, no dependencies).</summary>
internal static class WebPlayerAssets
{
    public const string IndexHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{TITLE}}</title>
<style>
  html, body { margin: 0; height: 100%; overflow: hidden; background: #111; font-family: system-ui, Segoe UI, sans-serif; }
  #c { width: 100%; height: 100%; display: block; background: linear-gradient(#5a8fd0, #bcd2ea); }
  #hud { position: fixed; left: 12px; top: 10px; color: #fff; font-size: 14px; line-height: 1.5;
         text-shadow: 0 1px 3px #000; pointer-events: none; white-space: pre; }
  #prompt { position: fixed; left: 50%; bottom: 14%; transform: translateX(-50%); color: #fff; font-size: 22px;
            background: rgba(0,0,0,.65); border: 1px solid #ffdc6e; padding: 8px 18px; display: none; pointer-events: none; }
  #msg { position: fixed; inset: 0; display: grid; place-items: center; color: #fff; font-size: 18px; pointer-events: none; }
</style>
</head>
<body>
<canvas id="c"></canvas>
<div id="hud"></div>
<div id="prompt"></div>
<div id="msg"></div>
<script src="data.js"></script>
<script src="player.js"></script>
</body>
</html>
""";

    public const string PlayerJs = """
(function () {
'use strict';
var G = window.GAME;
var canvas = document.getElementById('c');
var hud = document.getElementById('hud');
var msg = document.getElementById('msg');
var promptEl = document.getElementById('prompt');
var gl = canvas.getContext('webgl', { antialias: true, alpha: true, premultipliedAlpha: true });
if (!gl) { msg.textContent = 'WebGL is not available in this browser.'; return; }

// ---------- math (column-major 4x4, 3x3 as [row][col]) ----------
var D2R = Math.PI / 180;
function mul3(a, b) {
  var o = [[0,0,0],[0,0,0],[0,0,0]];
  for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) for (var k = 0; k < 3; k++) o[i][j] += a[i][k] * b[k][j];
  return o;
}
function rot3(e) {
  var x = e[0] * D2R, y = e[1] * D2R, z = e[2] * D2R;
  var cx = Math.cos(x), sx = Math.sin(x), cy = Math.cos(y), sy = Math.sin(y), cz = Math.cos(z), sz = Math.sin(z);
  var Rx = [[1,0,0],[0,cx,-sx],[0,sx,cx]];
  var Ry = [[cy,0,sy],[0,1,0],[-sy,0,cy]];
  var Rz = [[cz,-sz,0],[sz,cz,0],[0,0,1]];
  return mul3(Ry, mul3(Rx, Rz));
}
function mul4(a, b) {
  var o = new Array(16);
  for (var c = 0; c < 4; c++) for (var r = 0; r < 4; r++) {
    var s = 0;
    for (var k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k];
    o[c * 4 + r] = s;
  }
  return o;
}
function modelMatrix(o, R) {
  var s = o.scale, p = o.pos;
  return [R[0][0]*s[0], R[1][0]*s[0], R[2][0]*s[0], 0,
          R[0][1]*s[1], R[1][1]*s[1], R[2][1]*s[1], 0,
          R[0][2]*s[2], R[1][2]*s[2], R[2][2]*s[2], 0,
          p[0], p[1], p[2], 1];
}
function normalMatrix(o, R) {
  var inv = o.scale.map(function (v) { return Math.abs(v) > 1e-6 ? 1 / v : 0; });
  // R * diag(inv), column-major 3x3
  return [R[0][0]*inv[0], R[1][0]*inv[0], R[2][0]*inv[0],
          R[0][1]*inv[1], R[1][1]*inv[1], R[2][1]*inv[1],
          R[0][2]*inv[2], R[1][2]*inv[2], R[2][2]*inv[2]];
}
function viewMatrix(p, R) {
  var t = [-(R[0][0]*p[0] + R[1][0]*p[1] + R[2][0]*p[2]),
           -(R[0][1]*p[0] + R[1][1]*p[1] + R[2][1]*p[2]),
           -(R[0][2]*p[0] + R[1][2]*p[1] + R[2][2]*p[2])];
  return [R[0][0], R[0][1], R[0][2], 0,
          R[1][0], R[1][1], R[1][2], 0,
          R[2][0], R[2][1], R[2][2], 0,
          t[0], t[1], t[2], 1];
}
function projMatrix(fovDeg, aspect, n, f) {
  var t = 1 / Math.tan(fovDeg * D2R / 2);
  return [t / aspect, 0, 0, 0,  0, t, 0, 0,  0, 0, (f + n) / (n - f), -1,  0, 0, 2 * f * n / (n - f), 0];
}
function forward(R) { return [-R[0][2], -R[1][2], -R[2][2]]; }
function right(R) { return [R[0][0], R[1][0], R[2][0]]; }

// ---------- shader ----------
var VS = 'attribute vec3 aPos; attribute vec3 aNrm; attribute vec4 aCol; attribute vec2 aUV; varying vec2 vUV;' +
 'uniform mat4 uModel; uniform mat4 uVP; uniform mat3 uNrmM; uniform vec3 uCam; uniform vec3 uToLight; uniform vec3 uSun;' +
 'uniform float uAmb; uniform float uSpec; uniform float uShin; uniform float uUnlit; uniform float uDouble; uniform vec4 uTint;' +
 'varying vec4 vCol;' +
 'void main(){' +
 ' vec4 wp = uModel * vec4(aPos, 1.0); vec4 base = aCol * uTint; vec3 rgb;' +
 ' if (uUnlit > 0.5) { rgb = base.rgb; } else {' +
 '  vec3 n = uNrmM * aNrm; n = length(n) > 1e-6 ? normalize(n) : vec3(0.0, 1.0, 0.0);' +
 '  vec3 v = uCam - wp.xyz; v = length(v) > 1e-6 ? normalize(v) : vec3(0.0, 1.0, 0.0);' +
 '  if (uDouble > 0.5 && dot(n, v) < 0.0) n = -n;' +
 '  float diff = max(dot(n, uToLight), 0.0);' +
 '  rgb = base.rgb * (vec3(uAmb) + uSun * diff);' +
 '  if (diff > 0.0 && uSpec > 0.0) { vec3 h = normalize(uToLight + v); rgb += uSun * pow(max(dot(n, h), 0.0), uShin) * uSpec; }' +
 ' }' +
 ' vCol = vec4(clamp(rgb, 0.0, 1.0), 1.0); gl_Position = uVP * wp; vUV = aUV; }';
var FS = 'precision mediump float; varying vec4 vCol; varying vec2 vUV; uniform sampler2D uTex; uniform float uHasTex; uniform vec2 uTile;' +
 'void main(){ vec3 t = uHasTex > 0.5 ? texture2D(uTex, vUV * uTile).rgb : vec3(1.0); gl_FragColor = vec4(vCol.rgb * t, 1.0); }';

function compile(type, src) {
  var s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}
var prog = gl.createProgram();
gl.attachShader(prog, compile(gl.VERTEX_SHADER, VS));
gl.attachShader(prog, compile(gl.FRAGMENT_SHADER, FS));
gl.bindAttribLocation(prog, 0, 'aPos'); gl.bindAttribLocation(prog, 1, 'aNrm'); gl.bindAttribLocation(prog, 2, 'aCol'); gl.bindAttribLocation(prog, 3, 'aUV');
gl.linkProgram(prog);
if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
gl.useProgram(prog);
var U = {};
['uModel','uVP','uNrmM','uCam','uToLight','uSun','uAmb','uSpec','uShin','uUnlit','uDouble','uTint','uTex','uHasTex','uTile'].forEach(function (n) { U[n] = gl.getUniformLocation(prog, n); });
gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(1); gl.enableVertexAttribArray(2); gl.enableVertexAttribArray(3);
var uint32 = gl.getExtension('OES_element_index_uint');
gl.uniform1i(U.uTex, 0);
gl.enable(gl.DEPTH_TEST);

function buffer(target, data) {
  var b = gl.createBuffer(); gl.bindBuffer(target, b); gl.bufferData(target, data, gl.STATIC_DRAW); return b;
}
var meshes = {};
Object.keys(G.meshes).forEach(function (name) {
  var m = G.meshes[name];
  var big = m.p.length / 3 > 65535;
  if (big && !uint32) { console.warn('Mesh too large for this browser: ' + name); return; }
  meshes[name] = { p: buffer(gl.ARRAY_BUFFER, new Float32Array(m.p)), n: buffer(gl.ARRAY_BUFFER, new Float32Array(m.n)),
                   c: buffer(gl.ARRAY_BUFFER, new Float32Array(m.c)), t: buffer(gl.ARRAY_BUFFER, new Float32Array(m.t || new Array(m.p.length / 3 * 2).fill(0))),
                   i: buffer(gl.ELEMENT_ARRAY_BUFFER, big ? new Uint32Array(m.i) : new Uint16Array(m.i)), count: m.i.length, big: big, hasUV: !!m.t };
});

var zeroUV = null;
function bindAttribs(p, n, c, t) {
  gl.bindBuffer(gl.ARRAY_BUFFER, t || (zeroUV || (zeroUV = buffer(gl.ARRAY_BUFFER, new Float32Array(4096 * 2)))));
  gl.vertexAttribPointer(3, 2, gl.FLOAT, false, 0, 0);
  gl.bindBuffer(gl.ARRAY_BUFFER, p); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
  gl.bindBuffer(gl.ARRAY_BUFFER, n); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 0, 0);
  gl.bindBuffer(gl.ARRAY_BUFFER, c); gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 0, 0);
}

// textures (stored as data URIs in data.js so they also work when the page is opened from a file)
var textures = {};
function pot(n) { var p = 1; while (p < n) p <<= 1; return Math.min(p, 2048); }
Object.keys(G.textures || {}).forEach(function (path) {
  var img = new Image();
  img.onload = function () {
    var c = document.createElement('canvas');
    c.width = pot(img.width); c.height = pot(img.height);
    c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
    var tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, c);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
    textures[path] = tex;
  };
  img.src = G.textures[path];
});

// grid + axes (line lists)
function lineBuffers(lines) {
  var p = [], c = [];
  lines.forEach(function (l) { p.push(l[0], l[1], l[2], l[3], l[4], l[5]); c.push(l[6], l[7], l[8], 1, l[6], l[7], l[8], 1); });
  var n = new Array(p.length).fill(0);
  return { p: buffer(gl.ARRAY_BUFFER, new Float32Array(p)), n: buffer(gl.ARRAY_BUFFER, new Float32Array(n)),
           c: buffer(gl.ARRAY_BUFFER, new Float32Array(c)), count: lines.length * 2 };
}
var gridLines = [];
for (var gi = -20; gi <= 20; gi++) {
  var col = gi === 0 ? 0.5 : 0.75;
  gridLines.push([gi, 0, -20, gi, 0, 20, col, col, col]);
  gridLines.push([-20, 0, gi, 20, 0, gi, col, col, col]);
}
var grid = lineBuffers(gridLines);
var ringLines = [];
for (var ri = 0; ri < 48; ri++) {
  var a0 = ri / 48 * Math.PI * 2, a1 = (ri + 1) / 48 * Math.PI * 2, c0 = Math.cos(a0), s0 = Math.sin(a0), c1 = Math.cos(a1), s1 = Math.sin(a1);
  ringLines.push([c0, s0, 0, c1, s1, 0, 1, 1, 1]);   // XY
  ringLines.push([c0, 0, s0, c1, 0, s1, 1, 1, 1]);   // XZ
  ringLines.push([0, c0, s0, 0, c1, s1, 1, 1, 1]);   // YZ
}
var rings = lineBuffers(ringLines);
var axes = lineBuffers([[0,0,0,2,0,0,0.9,0.16,0.2], [0,0,0,0,2,0,0,0.9,0.19], [0,0,0,0,0,2,0,0.47,0.95]]);

// ---------- scenes ----------
var byName = {};
G.scenes.forEach(function (s) { byName[s.name.toLowerCase()] = s; });
var order = G.order.slice();
var reserved = null, nextReserved = null;
var scene = null, sceneIndex = -1, pending = null, sunPaused = false, sunBase = null;

function comp(o, type) { return o.comps[type] || null; }
function num(v, d) { return typeof v === 'number' ? v : d; }

function loadScene(name) {
  var s = byName[String(name).toLowerCase().replace(/^scenes[\/\\]/, '').replace(/\.json$/, '')];
  if (!s) { console.warn('Scene not found: ' + name); return; }
  var st = s.data.settings || {};
  var dir = st.sunDirection || [-0.4, -0.85, -0.3];
  var len = Math.hypot(dir[0], dir[1], dir[2]) || 1;
  var sc = st.sunColor || [255, 244, 222, 255];
  var objs = (s.data.objects || []).map(function (o) {
    var comps = {};
    (o.components || []).forEach(function (c) { comps[c.type] = c.properties || {}; });
    return { name: o.name, active: o.active !== false, pos: (o.position || [0,0,0]).slice(), rot: (o.rotation || [0,0,0]).slice(),
             scale: (o.scale || [1,1,1]).slice(), comps: comps, baseY: (o.position || [0,0,0])[1] };
  });
  scene = {
    name: s.name, objects: objs, time: 0,
    dir: [dir[0] / len, dir[1] / len, dir[2] / len],
    sunColor: sc, intensity: num(st.sunIntensity, 1), ambient: num(st.ambient, 0.28),
    showGrid: !!st.showGrid, showAxes: !!st.showAxes,
    sizeX: num(st.sizeX, 200), sizeZ: num(st.sizeZ, 200), clamp: st.clampCamera === true
  };
  scene.camera = objs.filter(function (o) { return o.active && comp(o, 'CameraComponent'); })[0] || null;
  var top = st.skyTop || [90, 143, 208, 255], bot = st.skyBottom || [188, 210, 234, 255];
  canvas.style.background = 'linear-gradient(rgb(' + top[0] + ',' + top[1] + ',' + top[2] + '), rgb(' + bot[0] + ',' + bot[1] + ',' + bot[2] + '))';
  sceneIndex = order.map(function (n) { return n.toLowerCase(); }).indexOf(s.name.toLowerCase());
  sunPaused = false;
}
function step(delta) {
  if (!order.length) return;
  var i = sceneIndex < 0 ? (delta > 0 ? -1 : 0) : sceneIndex;
  pending = order[(i + delta + order.length) % order.length];
}

// ---------- input ----------
var keys = {}, keyHit = {}, rmb = false, lookDX = 0, lookDY = 0;
function keyCode(name) {
  name = String(name).trim();
  if (/^[a-z]$/i.test(name)) return 'Key' + name.toUpperCase();
  if (/^[0-9]$/.test(name)) return 'Digit' + name;
  return name.charAt(0).toUpperCase() + name.slice(1).toLowerCase();   // Enter, Space, ...
}
window.addEventListener('keydown', function (e) {
  if (e.code === 'Tab') { e.preventDefault(); if (!e.repeat) step(e.shiftKey ? -1 : 1); return; }
  if (e.repeat) { keys[e.code] = true; return; }
  keys[e.code] = true; keyHit[e.code] = true;
  if (e.code === 'KeyL') sunPaused = !sunPaused;
  var m = /^Digit([1-9])$/.exec(e.code);
  if (m && +m[1] <= order.length) pending = order[+m[1] - 1];
});
window.addEventListener('keyup', function (e) { keys[e.code] = false; });
window.addEventListener('blur', function () { keys = {}; rmb = false; });
canvas.addEventListener('contextmenu', function (e) { e.preventDefault(); });
canvas.addEventListener('mousedown', function (e) {
  if (e.button === 2) { rmb = true; if (canvas.requestPointerLock) { try { canvas.requestPointerLock(); } catch (x) {} } }
});
window.addEventListener('mouseup', function (e) {
  if (e.button === 2) { rmb = false; if (document.exitPointerLock) document.exitPointerLock(); }
});
window.addEventListener('mousemove', function (e) { if (rmb) { lookDX += e.movementX || 0; lookDY += e.movementY || 0; } });

// ---------- update ----------
function updateScene(dt) {
  var sc = scene;
  sc.time += dt;
  sc.objects.forEach(function (o) {
    if (!o.active) return;
    var s = comp(o, 'Spinner');
    if (s) { var d = s.DegreesPerSecond || [0, 45, 0]; o.rot[0] += d[0] * dt; o.rot[1] += d[1] * dt; o.rot[2] += d[2] * dt; }
    var b = comp(o, 'Bobber');
    if (b) o.pos[1] = o.baseY + Math.sin(sc.time * num(b.Speed, 2)) * num(b.Amplitude, 0.4);
    if (comp(o, 'SunOrbit') && !sunPaused) {
      var a = sc.time * 0.5, x = Math.cos(a) * 0.8, z = Math.sin(a) * 0.8, l = Math.hypot(x, 0.9, z);
      sc.dir = [x / l, -0.9 / l, z / l];
    }
  });

  var cam = sc.camera;
  if (cam) {
    var fly = comp(cam, 'FlyCamera');
    if (fly && rmb) {
      var sens = num(fly.Sensitivity, 0.12);
      cam.rot[1] -= lookDX * sens;
      cam.rot[0] = Math.max(-89, Math.min(89, cam.rot[0] - lookDY * sens));
      var R = rot3(cam.rot), f = forward(R), r = right(R), mv = [0, 0, 0];
      function add(v, k) { mv[0] += v[0] * k; mv[1] += v[1] * k; mv[2] += v[2] * k; }
      function held(c) { return keys[c] && reserved !== c; }
      if (held('KeyW')) add(f, 1); if (held('KeyS')) add(f, -1);
      if (held('KeyD')) add(r, 1); if (held('KeyA')) add(r, -1);
      if (held('KeyE')) mv[1] += 1; if (held('KeyQ')) mv[1] -= 1;
      var ml = Math.hypot(mv[0], mv[1], mv[2]);
      if (ml > 0) {
        var speed = num(fly.Speed, 6) * ((keys.ShiftLeft || keys.ShiftRight) ? num(fly.FastMultiplier, 3) : 1);
        for (var i = 0; i < 3; i++) cam.pos[i] += mv[i] / ml * speed * dt;
      }
    }
    if (sc.clamp && cam) {
      cam.pos[0] = Math.max(-sc.sizeX / 2, Math.min(sc.sizeX / 2, cam.pos[0]));
      cam.pos[2] = Math.max(-sc.sizeZ / 2, Math.min(sc.sizeZ / 2, cam.pos[2]));
    }
    lookDX = lookDY = 0;
    reserved = nextReserved; nextReserved = null;
    sc.prompt = null;
    var best = Infinity;
    sc.objects.forEach(function (o) {
      var p = comp(o, 'ScenePortal');
      o.inRange = false;
      if (!o.active || !p || !p.TargetScene) return;
      var d = Math.hypot(cam.pos[0] - o.pos[0], cam.pos[1] - o.pos[1], cam.pos[2] - o.pos[2]);
      o.inRange = d <= num(p.Radius, 2.5);
      if (!o.inRange || sc.time < 1) return;
      if (p.RequireInteract === false) { pending = p.TargetScene; return; }
      var code = keyCode(p.InteractKey || 'E');
      nextReserved = code;
      if (d < best) {
        best = d;
        sc.prompt = p.Prompt || ('Press ' + (p.InteractKey || 'E').toUpperCase() + ' to enter ' + String(p.TargetScene).replace(/^Scenes[\/\\]/, ''));
      }
      if (!rmb && keyHit[code]) pending = p.TargetScene;
    });
    keyHit = {};
  } else { lookDX = lookDY = 0; keyHit = {}; }
}

// ---------- draw ----------
function setMaterial(tint, unlit, dbl, spec, shin) {
  gl.uniform4f(U.uTint, tint[0] / 255, tint[1] / 255, tint[2] / 255, tint[3] / 255);
  gl.uniform1f(U.uUnlit, unlit ? 1 : 0); gl.uniform1f(U.uDouble, dbl ? 1 : 0);
  gl.uniform1f(U.uSpec, spec); gl.uniform1f(U.uShin, shin);
  gl.uniform1f(U.uHasTex, 0);
}
var IDENT3 = [1,0,0, 0,1,0, 0,0,1], IDENT4 = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];

function draw() {
  var dpr = window.devicePixelRatio || 1;
  var w = Math.max(1, Math.floor(canvas.clientWidth * dpr)), h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
  gl.viewport(0, 0, w, h);
  gl.clearColor(0, 0, 0, 0);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  var sc = scene, cam = sc.camera;
  if (!cam) return;

  var R = rot3(cam.rot), cc = comp(cam, 'CameraComponent');
  var vp = mul4(projMatrix(num(cc.FieldOfView, 60), w / h, 0.05, 4000), viewMatrix(cam.pos, R));
  gl.uniformMatrix4fv(U.uVP, false, new Float32Array(vp));
  gl.uniform3f(U.uCam, cam.pos[0], cam.pos[1], cam.pos[2]);
  gl.uniform3f(U.uToLight, -sc.dir[0], -sc.dir[1], -sc.dir[2]);
  var k = sc.intensity / 255;
  gl.uniform3f(U.uSun, sc.sunColor[0] * k, sc.sunColor[1] * k, sc.sunColor[2] * k);
  gl.uniform1f(U.uAmb, sc.ambient);

  gl.uniformMatrix4fv(U.uModel, false, new Float32Array(IDENT4));
  gl.uniformMatrix3fv(U.uNrmM, false, new Float32Array(IDENT3));
  setMaterial([255, 255, 255, 255], true, false, 0, 1);
  if (sc.showGrid) { bindAttribs(grid.p, grid.n, grid.c); gl.drawArrays(gl.LINES, 0, grid.count); }
  if (sc.showAxes) { bindAttribs(axes.p, axes.n, axes.c); gl.drawArrays(gl.LINES, 0, axes.count); }

  if (G.development) {
    sc.objects.forEach(function (o) {
      var p = comp(o, 'ScenePortal');
      if (!o.active || !p) return;
      var r = Math.max(0.05, num(p.Radius, 2.5));
      var c = o.inRange ? [90, 255, 130, 255] : (p.RequireInteract === false ? [255, 230, 80, 255] : [255, 170, 60, 255]);
      gl.uniformMatrix4fv(U.uModel, false, new Float32Array([r,0,0,0, 0,r,0,0, 0,0,r,0, o.pos[0],o.pos[1],o.pos[2],1]));
      gl.uniformMatrix3fv(U.uNrmM, false, new Float32Array(IDENT3));
      setMaterial(c, true, false, 0, 1);
      bindAttribs(rings.p, rings.n, rings.c);
      gl.drawArrays(gl.LINES, 0, rings.count);
    });
  }

  sc.objects.forEach(function (o) {
    var mr = comp(o, 'MeshRenderer');
    if (!o.active || !mr) return;
    var m = meshes[mr.ModelPath ? 'model:' + mr.ModelPath : mr.PrimitiveName];
    if (!m) return;
    var Ro = rot3(o.rot);
    gl.uniformMatrix4fv(U.uModel, false, new Float32Array(modelMatrix(o, Ro)));
    gl.uniformMatrix3fv(U.uNrmM, false, new Float32Array(normalMatrix(o, Ro)));
    setMaterial(mr.Tint || [255, 255, 255, 255], !!mr.Unlit, mr.DoubleSided !== false, num(mr.Specular, 0.25), num(mr.Shininess, 32));
    bindAttribs(m.p, m.n, m.c, m.t);
    var tex = mr.TexturePath && m.hasUV ? textures[mr.TexturePath] : null;
    if (tex) {
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, tex);
      gl.uniform1f(U.uHasTex, 1); gl.uniform2f(U.uTile, num(mr.TileU, 1), num(mr.TileV, 1));
    }
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, m.i);
    gl.drawElements(gl.TRIANGLES, m.count, m.big ? gl.UNSIGNED_INT : gl.UNSIGNED_SHORT, 0);
  });
}

// ---------- loop ----------
var last = performance.now(), fps = 0, frames = 0, fpsT = 0;
function frame(now) {
  var dt = Math.min((now - last) / 1000, 0.1);
  last = now;
  if (pending !== null) { var p = pending; pending = null; loadScene(p); }
  updateScene(dt);
  draw();

  frames++; fpsT += dt;
  if (fpsT >= 0.5) { fps = Math.round(frames / fpsT); frames = 0; fpsT = 0; }
  var line = scene.name + (sceneIndex >= 0 ? '  (' + (sceneIndex + 1) + '/' + order.length + ')' : '');
  if (scene.prompt) { promptEl.textContent = scene.prompt; promptEl.style.display = 'block'; } else promptEl.style.display = 'none';
  hud.textContent = line + '\nHold right mouse + WASD/Q/E to fly, Shift = faster\nTab / Shift+Tab or 1-9: change scene, L: pause sun' +
    (G.development ? '\n' + fps + ' fps' : '');
  requestAnimationFrame(frame);
}

if (!order.length) { msg.textContent = 'This build contains no scenes.'; return; }
loadScene(order[0]);
requestAnimationFrame(frame);
})();
""";
}
