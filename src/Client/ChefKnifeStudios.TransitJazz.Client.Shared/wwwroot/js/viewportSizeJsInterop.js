let dotNetRef = null;
let handler = null;
let debounceTimer = null;

export function registerViewportSizeListener(reference, debounceMs) {
    // Idempotent: tear down any previous listener first.
    disposeViewportSizeListener();

    dotNetRef = reference;

    const notify = () => {
        const size = { x: window.innerWidth, y: window.innerHeight };
        dotNetRef
            ?.invokeMethodAsync('HandleViewportSizeChanged', size)
            .catch(err => console.error('HandleViewportSizeChanged failed:', err));
    };

    handler = () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(notify, debounceMs ?? 100);
    };

    window.addEventListener('resize', handler);

    notify(); // fire once for the initial size (consumers must subscribe first)
}

export function disposeViewportSizeListener() {
    if (handler) {
        window.removeEventListener('resize', handler);
        handler = null;
    }
    clearTimeout(debounceTimer);
    debounceTimer = null;
    dotNetRef = null;
}
