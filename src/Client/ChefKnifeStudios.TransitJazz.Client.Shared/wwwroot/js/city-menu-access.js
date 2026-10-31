export function observeCityMenu(dotNetReference) {
    const menu = document.querySelector('.city-fab-menu');
    let isOpen = false;

    function reportCurrentState() {
        const nextOpen = !!menu?.classList.contains('mdc-menu-surface--open');
        if (nextOpen === isOpen) return;
        isOpen = nextOpen;
        dotNetReference.invokeMethodAsync('HandleCityMenuVisibilityChanged', isOpen).catch(() => { });
    }

    const observer = new MutationObserver(reportCurrentState);
    if (menu) observer.observe(menu, { attributes: true, attributeFilter: ['class'] });
    reportCurrentState();
    return observer;
}

export function disconnectCityMenuObserver(observer) {
    observer?.disconnect();
}
