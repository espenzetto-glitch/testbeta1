// WebGL drawing + input for the real SharpEngine running as WebAssembly (SharpEngine.Web).
// C# calls sharp.begin / triangles / lines / end every frame; this file forwards keys and the mouse back to C#.
(function () {
  'use strict';
  var canvas = document.getElementById('c');
  var hudEl = document.getElementById('hud'), promptEl = document.getElementById('prompt'), msgEl = document.getElementById('msg');
  var barEl = document.querySelector('#bar i'), barBox = document.getElementById('bar');
  var gl = canvas.getContext('webgl2', { antialias: true, alpha: true }) || canvas.getContext('webgl', { antialias: true, alpha: true });

  function fail(text) { msgEl.textContent = text; msgEl.style.color = '#ffb4b4'; barBox.style.display = 'none'; console.error(text); }
  window.addEventListener('error', function (e) { fail('Error: ' + e.message); });
  window.addEventListener('unhandledrejection', function (e) { fail('Error: ' + (e.reason && e.reason.message || e.reason)); });
  if (!gl) { fail('This browser has no WebGL.'); return; }

  // ---------- shaders: one program for lit-vertex-colour triangles (optionally textured) and lines ----------
  var VS = 'attribute vec3 aPos; attribute vec4 aCol; attribute vec2 aUV; uniform mat4 uVP; varying vec4 vCol; varying vec2 vUV;' +
           'void main(){ vCol = aCol; vUV = aUV; gl_Position = uVP * vec4(aPos, 1.0); }';
  var FS = 'precision mediump float; varying vec4 vCol; varying vec2 vUV; uniform sampler2D uTex; uniform float uHasTex;' +
           'void main(){ vec4 c = vCol; if (uHasTex > 0.5) c *= texture2D(uTex, vUV); if (c.a < 0.01) discard; gl_FragColor = c; }';
  function shader(type, src) {
    var s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
    return s;
  }
  var prog = gl.createProgram();
  gl.attachShader(prog, shader(gl.VERTEX_SHADER, VS)); gl.attachShader(prog, shader(gl.FRAGMENT_SHADER, FS));
  gl.bindAttribLocation(prog, 0, 'aPos'); gl.bindAttribLocation(prog, 1, 'aCol'); gl.bindAttribLocation(prog, 2, 'aUV');
  gl.linkProgram(prog);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
  gl.useProgram(prog);
  var uVP = gl.getUniformLocation(prog, 'uVP'), uHasTex = gl.getUniformLocation(prog, 'uHasTex'), uTex = gl.getUniformLocation(prog, 'uTex');
  gl.uniform1i(uTex, 0);
  var vbo = gl.createBuffer();
  var white = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, white);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));

  // ---------- matrices (column-major) ----------
  function perspective(fovDeg, aspect, near, far) {
    var f = 1 / Math.tan(fovDeg * Math.PI / 360), nf = 1 / (near - far);
    return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0];
  }
  function sub(a, b) { return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]; }
  function cross(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }
  function dot(a, b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
  function norm(a) { var l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; }
  function lookAt(eye, target, up) {
    var z = norm(sub(eye, target)), x = norm(cross(up, z)), y = cross(z, x);
    return [x[0], y[0], z[0], 0, x[1], y[1], z[1], 0, x[2], y[2], z[2], 0, -dot(x, eye), -dot(y, eye), -dot(z, eye), 1];
  }
  function mul(a, b) {
    var o = new Array(16);
    for (var c = 0; c < 4; c++) for (var r = 0; r < 4; r++) {
      var s = 0; for (var k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k];
      o[c * 4 + r] = s;
    }
    return o;
  }
  function hex(n) { return '#' + ('000000' + (n & 0xffffff).toString(16)).slice(-6); }

  // ---------- textures (loaded from the build before the game starts) ----------
  var textures = {};
  function loadTexture(path, url) {
    return new Promise(function (resolve) {
      var img = new Image();
      img.onload = function () {
        var t = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, t);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
        var pot = (img.width & (img.width - 1)) === 0 && (img.height & (img.height - 1)) === 0;
        if (pot || gl instanceof WebGL2RenderingContext) {
          gl.generateMipmap(gl.TEXTURE_2D);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
        } else {
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
          gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
        }
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        textures[path] = t; resolve();
      };
      img.onerror = function () { console.warn('texture not loaded: ' + path); resolve(); };
      img.src = url;
    });
  }

  // ---------- what C# calls every frame ----------
  function floats(mv) { var u8 = mv.slice(); return new Float32Array(u8.buffer, u8.byteOffset, u8.byteLength >> 2); }

  var sharp = window.sharp = {
    begin: function (skyTop, skyBottom, cam) {
      var w = Math.max(1, Math.floor(canvas.clientWidth * (window.devicePixelRatio || 1)));
      var h = Math.max(1, Math.floor(canvas.clientHeight * (window.devicePixelRatio || 1)));
      if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
      canvas.style.background = 'linear-gradient(' + hex(skyTop) + ',' + hex(skyBottom) + ')';
      gl.viewport(0, 0, w, h);
      gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
      gl.enable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE);
      gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      var proj = perspective(cam[9], w / h, 0.05, 2000);
      var view = lookAt([cam[0], cam[1], cam[2]], [cam[3], cam[4], cam[5]], [cam[6], cam[7], cam[8]]);
      gl.useProgram(prog);
      gl.uniformMatrix4fv(uVP, false, new Float32Array(mul(proj, view)));
    },
    triangles: function (texture, mv, count) {
      var data = floats(mv);
      var tex = texture ? textures[texture] : null;
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STREAM_DRAW);
      gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(1); gl.enableVertexAttribArray(2);
      gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 36, 0);
      gl.vertexAttribPointer(1, 4, gl.FLOAT, false, 36, 12);
      gl.vertexAttribPointer(2, 2, gl.FLOAT, false, 36, 28);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, tex || white);
      gl.uniform1f(uHasTex, tex ? 1 : 0);
      gl.drawArrays(gl.TRIANGLES, 0, count);
    },
    lines: function (mv, count) {
      var data = floats(mv);
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STREAM_DRAW);
      gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(1); gl.disableVertexAttribArray(2);
      gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 28, 0);
      gl.vertexAttribPointer(1, 4, gl.FLOAT, false, 28, 12);
      gl.vertexAttrib2f(2, 0, 0);
      gl.uniform1f(uHasTex, 0);
      gl.drawArrays(gl.LINES, 0, count);
    },
    end: function () { },
    hud: function (text, prompt) {
      hudEl.textContent = text + '\nHold right mouse + WASD/Q/E to fly, Shift = faster\nTab / Shift+Tab or 1-9: change scene';
      promptEl.style.display = prompt ? 'block' : 'none';
      if (prompt) promptEl.textContent = prompt;
    },
    capture: function (on) {
      try { if (on) { if (document.pointerLockElement !== canvas) canvas.requestPointerLock(); } else if (document.pointerLockElement) document.exitPointerLock(); }
      catch (e) { /* the page may refuse; the mouse still works without the lock */ }
    },
    ready: function () { setTimeout(start, 30); }
  };

  // ---------- starting the game ----------
  var started = false;
  function call(name) { return DotNet.invokeMethod.apply(DotNet, ['SharpEngine.Web', name].concat([].slice.call(arguments, 1))); }

  async function start() {
    if (started) return; started = true;
    try {
      // wait until the .NET side answers (the runtime can still be finishing its start-up)
      for (var tries = 0; ; tries++) {
        try { call('ReleaseAll'); break; }
        catch (e) { if (tries > 150) throw new Error('The .NET runtime did not start: ' + (e && e.message || e)); await new Promise(function (r) { setTimeout(r, 100); }); }
      }
      var manifest = await (await fetch('game/manifest.json')).json();
      document.title = manifest.title || document.title;
      var total = manifest.files.length, done = 0;
      for (var i = 0; i < manifest.files.length; i++) {
        var f = manifest.files[i];
        var res = await fetch('game/' + f);
        if (!res.ok) throw new Error('Could not download ' + f + ' (' + res.status + ')');
        call('WriteFile', f, new Uint8Array(await res.arrayBuffer()));
        barEl.style.width = (++done / total * 100) + '%';
        if (/\.(png|jpe?g|bmp)$/i.test(f)) await loadTexture(f, 'game/' + f);
      }
      var err = call('Boot');
      if (err) { fail('The game could not start:\n' + err); return; }
      msgEl.style.display = 'none'; barBox.style.display = 'none';
      wireInput(); canvas.focus();
      var last = performance.now();
      (function frame(now) {
        var dt = (now - last) / 1000; last = now;
        try { call('Frame', dt); } catch (e) { fail('Error: ' + e.message); return; }
        requestAnimationFrame(frame);
      })(last);
    } catch (e) { fail('Error: ' + (e && e.message || e)); }
  }

  function wireInput() {
    var block = { Tab: 1, Space: 1, ArrowUp: 1, ArrowDown: 1, ArrowLeft: 1, ArrowRight: 1 };
    window.addEventListener('keydown', function (e) { if (e.repeat) return; call('Key', e.code, true); if (block[e.code]) e.preventDefault(); });
    window.addEventListener('keyup', function (e) { call('Key', e.code, false); });
    window.addEventListener('blur', function () { call('ReleaseAll'); });
    canvas.addEventListener('mousedown', function (e) { canvas.focus(); call('Mouse', e.button, true); e.preventDefault(); });
    window.addEventListener('mouseup', function (e) { call('Mouse', e.button, false); });
    canvas.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    window.addEventListener('mousemove', function (e) { if (e.movementX || e.movementY) call('MouseMove', e.movementX, e.movementY); });
  }
})();
