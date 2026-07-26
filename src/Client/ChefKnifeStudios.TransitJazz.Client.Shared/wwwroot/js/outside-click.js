export function addOutsideClickListener(elementId, dotNetHelper) {
    const listener = (event) => {
        // A target removed from the DOM during this same click (e.g. a button that
        // hides itself on click) is no longer connected — contains() would report it
        // as "outside". It came from inside our UI, so ignore it.
        if (event.target && !event.target.isConnected) return;
        const el = document.getElementById(elementId);
        if (el && !el.contains(event.target)) {
            dotNetHelper.invokeMethodAsync('HandleOutsideClick', elementId);
        }
    };
    document.addEventListener('click', listener);
    return listener;
}

export function removeOutsideClickListener(listener) {
    document.removeEventListener('click', listener);
}
