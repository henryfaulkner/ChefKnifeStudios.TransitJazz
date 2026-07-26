export function addVisibilityChangeListener(dotNetHelper) {
    const listener = () => {
        dotNetHelper.invokeMethodAsync('HandleVisibilityChanged', document.hidden);
    };
    document.addEventListener('visibilitychange', listener);
    return listener;
}

export function removeVisibilityChangeListener(listener) {
    document.removeEventListener('visibilitychange', listener);
}

export function getHidden() {
    return document.hidden;
}
