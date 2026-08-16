// Parallel Factory MES クライアント用JS（帳票印刷・バーコード/QRスキャン・CSVダウンロード。Spec.md 3.8）
window.mesApp = {
    // 帳票・ラベル出力（ブラウザの印刷ダイアログ経由でPDF保存も可能）
    print: () => window.print(),

    // APIから取得したファイル（Base64）をダウンロードさせる（マスタのCSV出力）
    downloadFile: (fileName, base64, contentType) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        const url = URL.createObjectURL(new Blob([bytes], { type: contentType || 'text/csv' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    scanner: {
        _stream: null,
        _stopped: true,

        // カメラAPIによるスキャン可否（BarcodeDetector未対応ブラウザではUSB HIDリーダー入力のみ利用）
        isSupported: () => 'BarcodeDetector' in window && !!navigator.mediaDevices,

        start: async function (videoId, dotNetRef) {
            if (!this.isSupported()) {
                return false;
            }
            const video = document.getElementById(videoId);
            if (!video) {
                return false;
            }
            try {
                this._stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: 'environment' }
                });
            } catch (e) {
                return false;
            }
            this._stopped = false;
            video.srcObject = this._stream;
            await video.play();

            const detector = new BarcodeDetector();
            const loop = async () => {
                if (this._stopped) {
                    return;
                }
                try {
                    const codes = await detector.detect(video);
                    if (codes.length > 0 && codes[0].rawValue) {
                        const value = codes[0].rawValue;
                        this.stop();
                        await dotNetRef.invokeMethodAsync('OnScanned', value);
                        return;
                    }
                } catch (e) {
                    // 検出失敗は無視して次フレームへ
                }
                setTimeout(loop, 200);
            };
            setTimeout(loop, 200);
            return true;
        },

        stop: function () {
            this._stopped = true;
            if (this._stream) {
                this._stream.getTracks().forEach(t => t.stop());
                this._stream = null;
            }
        }
    }
};
