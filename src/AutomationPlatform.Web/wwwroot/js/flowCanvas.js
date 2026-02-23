window.flowCanvas = (() => {
  function ensure(host) {
    if (!host.__flowState) {
      host.__flowState = { x: 0, y: 0, zoom: 1, draggingPan: false, lastX: 0, lastY: 0, dotNetRef: null };
    }
    return host.__flowState;
  }

  function emit(host) {
    const s = ensure(host);
    if (s.dotNetRef) {
      s.dotNetRef.invokeMethodAsync('OnViewportChanged', { x: s.x, y: s.y, zoom: s.zoom });
    }
  }

  function init(host, dotNetRef) {
    const s = ensure(host);
    s.dotNetRef = dotNetRef;

    if (s.initialized) return;
    s.initialized = true;

    host.addEventListener('wheel', (e) => {
      if (!e.ctrlKey && !e.metaKey) return;
      e.preventDefault();
      const delta = e.deltaY < 0 ? 1.08 : 0.92;
      s.zoom = Math.min(2.5, Math.max(0.35, s.zoom * delta));
      emit(host);
    }, { passive: false });

    host.addEventListener('pointerdown', (e) => {
      if (e.button !== 1 && !(e.button === 0 && e.altKey)) return;
      s.draggingPan = true;
      s.lastX = e.clientX;
      s.lastY = e.clientY;
      host.setPointerCapture?.(e.pointerId);
    });

    host.addEventListener('pointermove', (e) => {
      if (!s.draggingPan) return;
      const dx = e.clientX - s.lastX;
      const dy = e.clientY - s.lastY;
      s.lastX = e.clientX;
      s.lastY = e.clientY;
      s.x += dx;
      s.y += dy;
      emit(host);
    });

    host.addEventListener('pointerup', (e) => {
      s.draggingPan = false;
      host.releasePointerCapture?.(e.pointerId);
    });
    host.addEventListener('pointercancel', () => { s.draggingPan = false; });
  }

  function dispose(host) {
    if (host && host.__flowState) {
      host.__flowState.dotNetRef = null;
    }
  }

  function clientToWorld(host, clientX, clientY) {
    const s = ensure(host);
    const rect = host.getBoundingClientRect();
    return {
      x: (clientX - rect.left - s.x) / s.zoom,
      y: (clientY - rect.top - s.y) / s.zoom
    };
  }

  return { init, dispose, clientToWorld };
})();
