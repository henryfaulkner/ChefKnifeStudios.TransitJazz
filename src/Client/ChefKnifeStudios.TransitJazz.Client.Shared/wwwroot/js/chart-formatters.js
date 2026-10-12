export const chartJsCallbacks = {
    formatStatsTooltip(context) {
        const lines = context?.raw?.tooltipLines;
        return Array.isArray(lines) && lines.length > 0
            ? lines
            : `${context?.dataset?.label ?? ""}: ${context?.parsed?.y ?? "—"}`;
    },

    formatStatsElapsedTick(value) {
        const datasets = this?.chart?.data?.datasets ?? [];
        for (const dataset of datasets) {
            const point = dataset.data?.find(item =>
                item && typeof item === "object" && Number(item.x) === Number(value));
            if (point?.tickLabel) return point.tickLabel;
        }
        return `${value} h`;
    },

    disableStatsLegendToggle() {
        // Category hiding would change the meaning of the cumulative stack.
    },

    toggleStatsCityLegend(event, legendItem, legend) {
        const chart = legend?.chart;
        const index = legendItem?.datasetIndex;
        if (!chart || !Number.isInteger(index)) return;

        if (chart.isDatasetVisible(index)) {
            chart.hide(index);
            legendItem.hidden = true;
        } else {
            chart.show(index);
            legendItem.hidden = false;
        }
    }
};

const preparedModules = new Map();

function importPreparedModule(url) {
    let promise = preparedModules.get(url);
    if (!promise) {
        promise = import(url).catch(error => {
            if (preparedModules.get(url) === promise) preparedModules.delete(url);
            throw error;
        });
        preparedModules.set(url, promise);
    }
    return promise;
}

export async function prepareStatsChartAssets(chartJsUrl, callbacksModuleUrl) {
    if (typeof chartJsUrl !== "string" || chartJsUrl.length === 0
        || typeof callbacksModuleUrl !== "string" || callbacksModuleUrl.length === 0) {
        throw new Error("Chart.js asset URLs are not configured.");
    }
    await Promise.all([
        importPreparedModule(chartJsUrl),
        importPreparedModule(callbacksModuleUrl)
    ]);
}
