// Computes how many fixed-width book cards fit across the library grid and notifies
// .NET whenever that count changes (initial layout + window/container resize). The page
// virtualizes by row, so it needs to know the live column count to chunk rows correctly.
window.bookTrakLibraryGrid = (function () {
    let observer = null;
    let lastColumns = 0;

    function computeColumns(container, cardWidth, gap) {
        // clientWidth excludes the scrollbar, so cards never overflow into it.
        const width = container.clientWidth;
        if (width <= 0) {
            return 1;
        }
        // n cards take n*cardWidth + (n-1)*gap of width. Solve for the largest n that fits.
        return Math.max(1, Math.floor((width + gap) / (cardWidth + gap)));
    }

    function observe(container, dotNetRef, cardWidth, gap) {
        if (!container) {
            return 1;
        }

        lastColumns = computeColumns(container, cardWidth, gap);

        observer = new ResizeObserver(() => {
            const columns = computeColumns(container, cardWidth, gap);
            if (columns !== lastColumns) {
                lastColumns = columns;
                dotNetRef.invokeMethodAsync('OnColumnsChanged', columns);
            }
        });
        observer.observe(container);

        return lastColumns;
    }

    function disconnect() {
        if (observer) {
            observer.disconnect();
            observer = null;
        }
    }

    return { observe, disconnect };
})();
