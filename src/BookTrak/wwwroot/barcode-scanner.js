// Barcode → ISBN scanning. Loopback (127.0.0.1/localhost) counts as a secure context, so
// getUserMedia works over plain HTTP here — no TLS needed. Prefers the native BarcodeDetector
// (Chromium) and falls back to the vendored @zxing/browser decoder for other browsers.
window.bookTrakBarcodeScanner = (function () {
    let stream = null;
    let detectLoopHandle = null;
    let zxingReader = null;

    function hasNativeDetector() {
        return 'BarcodeDetector' in window;
    }

    async function start(videoElementId, dotNetRef) {
        const video = document.getElementById(videoElementId);
        if (!video) {
            return { ok: false, error: 'Video element not found.' };
        }

        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
        } catch (err) {
            return { ok: false, error: 'Camera access was denied or unavailable: ' + err.message };
        }

        video.srcObject = stream;
        await video.play();

        if (hasNativeDetector()) {
            startNativeDetection(video, dotNetRef);
            return { ok: true, mode: 'native' };
        }

        if (window.ZXingBrowser) {
            startZXingDetection(video, dotNetRef);
            return { ok: true, mode: 'zxing' };
        }

        stop();
        return { ok: false, error: 'No barcode decoder is available in this browser. Enter the ISBN manually.' };
    }

    function startNativeDetection(video, dotNetRef) {
        const detector = new window.BarcodeDetector({ formats: ['ean_13', 'upc_a'] });

        const tick = async () => {
            if (!stream) {
                return;
            }
            try {
                const codes = await detector.detect(video);
                if (codes.length > 0) {
                    const value = codes[0].rawValue;
                    stop();
                    dotNetRef.invokeMethodAsync('OnBarcodeDetected', value);
                    return;
                }
            } catch {
                // transient decode failure — keep trying
            }
            detectLoopHandle = requestAnimationFrame(tick);
        };

        detectLoopHandle = requestAnimationFrame(tick);
    }

    function startZXingDetection(video, dotNetRef) {
        zxingReader = new window.ZXingBrowser.BrowserMultiFormatReader();
        zxingReader.decodeFromVideoElement(video, (result, err) => {
            if (result) {
                const value = result.getText();
                stop();
                dotNetRef.invokeMethodAsync('OnBarcodeDetected', value);
            }
        });
    }

    function stop() {
        if (detectLoopHandle) {
            cancelAnimationFrame(detectLoopHandle);
            detectLoopHandle = null;
        }
        if (zxingReader) {
            try { zxingReader.reset(); } catch { /* ignore */ }
            zxingReader = null;
        }
        if (stream) {
            stream.getTracks().forEach(t => t.stop());
            stream = null;
        }
    }

    return { start, stop, hasNativeDetector };
})();
