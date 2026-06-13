export function addOutsideClickListener(elementId, dotNetHelper) {
    const listener = (event) => {
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
