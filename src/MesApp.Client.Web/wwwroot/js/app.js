// Parallel Factory MES クライアント用JS（帳票印刷・バーコード/QRスキャン。Spec.md 3.8）
window.mesApp = {
    // 帳票・ラベル出力（ブラウザの印刷ダイアログ経由でPDF保存も可能）
    print: () => window.print(),

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
