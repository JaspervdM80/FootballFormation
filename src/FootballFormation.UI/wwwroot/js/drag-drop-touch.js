// Touch shim for the HTML5 drag & drop API.
//
// iOS Safari and Android Chrome never fire drag events from touch input, so the
// formation builder's @ondragstart/@ondragover/@ondrop handlers would be dead on
// phones. This shim watches touches on [draggable="true"] elements and re-dispatches
// them as synthetic drag events, which Blazor picks up like native ones.
//
// Scope note: the app's handlers keep all drag state in the Blazor component, so the
// synthetic events carry only coordinates and a stub dataTransfer — this is not a
// general-purpose polyfill.
(function () {
    'use strict';

    const MOVE_THRESHOLD_PX = 8; // ignore jitter before committing to a drag

    let source = null;      // the [draggable] element the touch started on
    let ghost = null;       // visual copy that follows the finger
    let dragging = false;
    let startX = 0, startY = 0;

    function fireDragEvent(type, target, touch) {
        // Blazor silently drops drag events whose dataTransfer is null (its
        // DragEventArgs serializer reads dataTransfer.files/items/types), and it
        // ignores plain Events with a drag type name entirely — so this must be a
        // real DragEvent carrying a non-null dataTransfer.
        const init = { bubbles: true, cancelable: true, clientX: touch.clientX, clientY: touch.clientY };
        let ev;
        try {
            ev = new DragEvent(type, { ...init, dataTransfer: new DataTransfer() });
        } catch {
            // No DataTransfer constructor (older iOS Safari): shadow the read-only
            // property with a stub exposing the fields Blazor reads.
            ev = new DragEvent(type, init);
            Object.defineProperty(ev, 'dataTransfer', {
                value: {
                    dropEffect: 'move', effectAllowed: 'move',
                    files: [], items: [], types: [],
                    setData() { }, getData() { return ''; }, clearData() { }, setDragImage() { }
                }
            });
        }
        target.dispatchEvent(ev);
    }

    function elementUnderTouch(touch) {
        // The ghost sits under the finger; hide it or we would only ever hit it.
        if (ghost) ghost.style.display = 'none';
        const el = document.elementFromPoint(touch.clientX, touch.clientY);
        if (ghost) ghost.style.display = '';
        return el;
    }

    function createGhost(el, touch) {
        ghost = el.cloneNode(true);
        ghost.style.cssText =
            'position:fixed;z-index:9999;pointer-events:none;opacity:0.7;margin:0;' +
            `width:${el.offsetWidth}px;height:${el.offsetHeight}px;`;
        moveGhost(touch);
        document.body.appendChild(ghost);
    }

    function moveGhost(touch) {
        ghost.style.left = (touch.clientX - ghost.offsetWidth / 2) + 'px';
        ghost.style.top = (touch.clientY - ghost.offsetHeight / 2) + 'px';
    }

    function reset() {
        if (ghost) ghost.remove();
        source = null;
        ghost = null;
        dragging = false;
    }

    document.addEventListener('touchstart', (e) => {
        const el = e.target.closest ? e.target.closest('[draggable="true"]') : null;
        if (!el) return;
        source = el;
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
    }, { passive: true });

    document.addEventListener('touchmove', (e) => {
        if (!source) return;
        const touch = e.touches[0];

        if (!dragging) {
            if (Math.hypot(touch.clientX - startX, touch.clientY - startY) < MOVE_THRESHOLD_PX) return;
            dragging = true;
            fireDragEvent('dragstart', source, touch);
            createGhost(source, touch);
        }

        e.preventDefault(); // we own this gesture now — no scrolling
        moveGhost(touch);
        const over = elementUnderTouch(touch);
        if (over) fireDragEvent('dragover', over, touch);
    }, { passive: false });

    document.addEventListener('touchend', (e) => {
        if (!source) return;
        if (dragging) {
            const touch = e.changedTouches[0];
            const target = elementUnderTouch(touch);
            if (target) fireDragEvent('drop', target, touch);
            fireDragEvent('dragend', source, touch);
        }
        reset(); // a tap without movement falls through to the normal click
    });

    document.addEventListener('touchcancel', reset);
})();
