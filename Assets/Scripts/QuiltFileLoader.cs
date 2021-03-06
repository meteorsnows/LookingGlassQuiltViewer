using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Kirurobo;
using LookingGlass;
using SFB;
using UnityEngine.Video;

public class QuiltFileLoader : MonoBehaviour
{
    WindowController window;
    Texture2D texture;
    VideoPlayer videoPlayer;
    RenderTexture videoRenderTexture;
    Holoplay holoplay;
    Quilt.Settings defaultTiling;

    public TMPro.TextMeshPro messageText;       // ファイル名等表示用のText
    public GameObject prevIndicator;            // 前のファイルへ移動時に表示するオブジェクト
    public GameObject nextIndicator;            // 次のファイルへ移動時に表示するオブジェクト

    public int frameRateForStill = 10;          // 静止画表示時のフレームレート指定 [fps]
    public int frameRateForMovie = 60;          // 動画再生時のフレームレート指定 [fps]

    static readonly string[] imageExtensions = { "png", "jpg", "jpeg" };
    static readonly string[] movieExtensions = { "mp4", "webm", "mov", "avi" };


    /// <summary>
    /// 読み込み待ちならtrueにする
    /// </summary>
    bool isLoading = false;

    /// <summary>
    /// カーソルが元々表示されているか
    /// </summary>
    bool isCursorVisible = true;

    /// <summary>
    /// メッセージを表示した場合、それを消去する時刻[s]をもつ
    /// </summary>
    float messageClearTime = 0;

    /// <summary>
    /// スライドショー対象の指定ファイル。
    /// これが空ならば現在開いたファイルと同じディレクトリを探す。
    /// </summary>
    List<string> targetFiles = new List<string>();

    /// <summary>
    /// 現在表示されている画像ファイルのパス
    /// </summary>
    string currentFile;


    // Use this for initialization
    void Start()
    {
        // ファイルドロップなどを扱うためのWindowControllerインスタンスを取得
        window = FindObjectOfType<WindowController>();
        window.OnFilesDropped += Window_OnFilesDropped;

        // Quiltのインスタンスを取得
        holoplay = FindObjectOfType<Holoplay>();
        defaultTiling = Quilt.GetPreset(holoplay.quiltPreset);   // Tilingの初期設定を記憶しておく
        holoplay.background = new Color(0, 0, 0, 0);             // 背景は透明にする

        // VideoPlayerのインスタンスを取得
        videoPlayer = FindObjectOfType<VideoPlayer>();
        if (videoPlayer)
        {
            //videoRenderTexture = new RenderTexture(4096, 4096, 24);
            videoRenderTexture = new RenderTexture(4096, 4096, 32);
            videoPlayer.targetTexture = videoRenderTexture;

            videoPlayer.seekCompleted += VideoPlayer_seekCompleted;
        }

        // フレームレートを指定
        Application.targetFrameRate = frameRateForStill;

        // 操作に対する表示は非表示にしておく
        if (nextIndicator) nextIndicator.SetActive(false);
        if (prevIndicator) prevIndicator.SetActive(false);

        // サンプルの画像を読み込み
        LoadFile(Path.Combine(Application.streamingAssetsPath, "startup.png"));

        // メッセージ欄を最初に消去
        ShowMessage("");

        // カーソルを表示するか否かを記憶
        isCursorVisible = Cursor.visible;

    }

    void Update()
    {
        // 操作できるのはファイル読み込み待ちでないときだけ
        if (!isLoading)
        {
            if (Input.GetKeyUp(KeyCode.Escape))
            {
                Quit();
            }
            // [O] キーまたは右クリックでファイル選択ダイアログを開く
            if (Input.GetKeyDown(KeyCode.O) || Input.GetMouseButtonUp(1))
            {
                OpenFile();
            }

            // [S] キーを押されたタイミングでカーソルや情報を非表示に
            if (Input.GetKeyDown(KeyCode.S))
            {
                ShowMessage("");
                Cursor.visible = false;
            }
            // [S] キーが離されたタイミングで現在の画面を保存。カーソルを写さないため非表示化とタイミングをずらす
            if (Input.GetKeyUp(KeyCode.S))
            {
                SaveFile();
            }

            // 前の画像
            if (ButtonManager.GetButtonDown(ButtonType.LEFT) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                ShowMessage("");    // ファイル名が表示されていれば消す
                LoadFile(GetNextFile(-1));
            }

            // 次の画像
            if (ButtonManager.GetButtonDown(ButtonType.RIGHT) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                ShowMessage("");    // ファイル名が表示されていれば消す
                LoadFile(GetNextFile(1));
            }

            // 開かれているファイル名を表示
            if (ButtonManager.GetButtonDown(ButtonType.CIRCLE))
            {
                ShowFilename(currentFile);
            }
        }

        // 左ボタンが押されていることを表示
        if (prevIndicator) prevIndicator.SetActive((ButtonManager.GetButton(ButtonType.LEFT) || Input.GetKey(KeyCode.LeftArrow)));

        // 右ボタンが押されていることを表示
        if (nextIndicator) nextIndicator.SetActive((ButtonManager.GetButton(ButtonType.RIGHT) || Input.GetKey(KeyCode.RightArrow)));

        //UpdateVideo();
        UpdateMessage();
        //UpdateIndicator();
    }
    
    /// <summary>
    /// 終了処理
    /// </summary>
    private void Quit() {
#if UNITY_EDITOR
        // エディタ上なら、再生を終了
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // スタンドアローンなら、アプリケーションを終了
        Application.Quit();
#endif
    }

    /// <summary>
    /// 動画再生中ならばテクスチャを更新
    /// </summary>
    private void UpdateVideo()
    {
        if (videoPlayer && videoPlayer.isPlaying && texture)
        {
            // 動画再生中ならば、内容を Texture2D として複製。たぶん重い。
            RenderTexture currentRenderTexture = RenderTexture.active;
            RenderTexture.active = videoRenderTexture;
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture.Apply();
            RenderTexture.active = currentRenderTexture;
        }
    }

    /// <summary>
    /// メッセージを指定時刻に消す
    /// </summary>
    private void UpdateMessage()
    {
        if (messageClearTime > 0)
        {
            if (messageClearTime < Time.time)
            {
                messageText.text = "";
                messageClearTime = 0;
            }
        }
    }

    /// <summary>
    /// 一定時間で消えるメッセージを表示
    /// </summary>
    /// <param name="text">メッセージ文字列</param>
    /// <param name="lifetime">消えるまでの時間[s]</param>
    private void ShowMessage(string text, float lifetime = 5f)
    {
        if (messageText)
        {
            messageText.text = text;
            messageClearTime = Time.time + lifetime;
        }
    }

    private void ShowFilename(string path)
    {
        string dir = Path.GetDirectoryName(path);
        string file = Path.GetFileName(path);
        ShowMessage(
            "<size=10><color=#FFFFFF>" + file + "</color></size>"
            + System.Environment.NewLine
            + "<size=6><color=#00FF00>" + dir + "</color></size>"
            );
    }

    /// <summary>
    /// 現在の画面をPNGで保存
    /// </summary>
    private void SaveFile()
    {
        StartCoroutine(SaveFileCoroutine());
    }

    /// <summary>
    /// フレーム描画後に画像を保存
    /// </summary>
    /// <returns></returns>
    private IEnumerator SaveFileCoroutine()
    {
        yield return new WaitForEndOfFrame();

        // 現在のRenderTextureの内容からTexture2Dを作成
        RenderTexture renderTexture = RenderTexture.active;
        int w = Screen.width;
        int h = Screen.height;
        Texture2D texture = new Texture2D(w, h, TextureFormat.ARGB32, false);
        texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        texture.Apply();

        // PNGに変換
        byte[] rawData = texture.EncodeToPNG();
        Destroy(texture);

        // 日時を基にファイル名を決定
        string file = "LookingGlass_" + System.DateTime.Now.ToString("yyyyMMdd_hhmmss") + ".png";

        // 書き出し
        System.IO.File.WriteAllBytes(file, rawData);
        Debug.Log("Saved " + file);

        // 保存したというメッセージを表示
        ShowMessage("Saved " + file);

        // カーソル表示を元に戻す
        Cursor.visible = isCursorVisible;
    }


    /// <summary>
    /// 画像を読み込み
    /// </summary>
    /// <param name="uri">Path.</param>
    private void LoadFile(string path) {
        if (string.IsNullOrEmpty(path)) return;

        isLoading = true;
        currentFile = path;

        // 動画は停止し初期状態に戻す
        if (videoPlayer)
        {
            videoPlayer.targetTexture = videoRenderTexture;
            videoPlayer.Stop();
        }

        if (CheckMovieFile(path))
        {    // 動画を開く場合
            Application.targetFrameRate = frameRateForMovie;
            StartCoroutine("LoadMovieFileCoroutine", path);
        }
        else
        {   // 静止画を開く場合
            Application.targetFrameRate = frameRateForStill;

            // もし動画が再生されていれば停止しておく
            if (videoPlayer && videoPlayer.isPlaying)
            {
                videoPlayer.Stop();
            }

            string uri = new System.Uri(path).AbsoluteUri;
            //Debug.Log("Loading: " + uri);

            StartCoroutine("LoadImageFileCoroutine", uri);
        }
    }

    /// <summary>
    /// コルーチンで画像ファイル読み込み
    /// </summary>
    /// <param name="uri"></param>
    /// <returns></returns>
    IEnumerator LoadImageFileCoroutine(string uri)
    {
        // 読み込み
        WWW www = new WWW(uri);
        yield return www;

        // 前のtextureを破棄
        Destroy(texture);

        // Quiltを読み込み
        texture = www.texture;
        holoplay.customQuiltSettings = GetTilingType(texture);
        holoplay.quiltPreset = Quilt.Preset.Custom;
        holoplay.overrideQuilt = texture;

        holoplay.SetupQuilt();
        holoplay.quiltRT.filterMode = FilterMode.Bilinear;

        //Debug.Log("Estimaged tiling: " + holoplay.quiltSettings.numViews);     // 選択されたTiling

    
        // 念のため毎回GCをしてみる…
        System.GC.Collect();

        // フラグを読み込み完了とする
        isLoading = false;
    }


    /// <summary>
    /// コルーチンで動画ファイル読み込み
    /// </summary>
    /// <param name="uri"></param>
    /// <returns></returns>
    IEnumerator LoadMovieFileCoroutine(string uri)
    {
        ShowMessage("Loading the movie...   ");
        holoplay.overrideQuilt = null;     // 読み込み開始を伝えるため、前の画像は消してしまう

        // 動画を読み込み
        videoPlayer.url = uri;
        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared)
        {
            yield return null;
        }

        // 前のtextureを破棄
        //Destroy(texture);

        yield return new WaitForSecondsRealtime(0.1f);
        videoPlayer.Play();

        ShowMessage("Loading the movie......", 0.5f);
        yield return new WaitForSecondsRealtime(0.5f);  // フレームが表示されそうな時間、強制的に待つ
        //Debug.Log("Play movie");

        // Seek
        videoPlayer.frame = 0;

        yield return new WaitForEndOfFrame();

        // 念のため読み込み毎にGCをしてみる…
        System.GC.Collect();

        // フラグを読み込み完了とする
        isLoading = false;
    }

    /// <summary>
    /// 動画の準備が整ったらタイル数推定を行って描画開始
    /// </summary>
    /// <param name="source"></param>
    private void VideoPlayer_seekCompleted(VideoPlayer source)
    {
        if (holoplay)
        {
            // 前のtextureを破棄
            Destroy(texture);

            texture = new Texture2D(videoRenderTexture.width, videoRenderTexture.height);

            RenderTexture currentRenderTexture = RenderTexture.active;
            RenderTexture.active = videoRenderTexture;
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture.Apply();
            RenderTexture.active = currentRenderTexture;

            holoplay.customQuiltSettings = GetTilingType(texture);
            holoplay.quiltPreset = Quilt.Preset.Custom;
            holoplay.SetupQuilt();

            //holoplay.overrideQuilt = texture;
            holoplay.overrideQuilt = null;      // ←動画の場合直接 quiltRT に描画させるため

            holoplay.quiltRT.filterMode = FilterMode.Bilinear;
            videoPlayer.targetTexture = holoplay.quiltRT;   // 動画の描画先をこちらにすることで表示

            //Debug.Log("Estimaged tiling: " + holoplay.customQuiltSettings.numViews);     // 選択されたTiling
        }
    }

    /// <summary>
    /// 指定ディレクトリ内の画像をターゲットのリストに追加
    /// </summary>
    /// <param name="directory"></param>
    /// <param name="list"></param>
    private void AddTargetDirectory(string directory, ref List<string> list)
    {
        string[] allFiles = Directory.GetFiles(directory);
        foreach (string path in allFiles)
        {
            if (CheckImageFile(path) || CheckMovieFile(path))
            {
                list.Add(path);
            }
        }
    }

    /// <summary>
    /// 指定ファイルが対象となる画像かどうかを判別
    /// 現状、JPEGかPNGなら通す
    /// </summary>
    /// <param name="path">ファイルのパス</param>
    /// <returns>対象の形式ならtrue</returns>
    private bool CheckImageFile(string path)
    {
        // 先頭のピリオドは除去して小文字にした拡張子
        string ext = Path.GetExtension(path).Substring(1).ToLower();

        foreach (string extension in imageExtensions)
        {
            if (extension == ext) return true;
        }
        return false;
    }

    /// <summary>
    /// 指定ファイルが対象となる画像かどうかを判別
    /// 現状、JPEGかPNGなら通す
    /// </summary>
    /// <param name="path">ファイルのパス</param>
    /// <returns>対象の形式ならtrue</returns>
    private bool CheckMovieFile(string path)
    {
        // 先頭のピリオドは除去して小文字にした拡張子
        string ext = Path.GetExtension(path).Substring(1).ToLower();

        foreach (string extension in movieExtensions)
        {
            if (extension == ext) return true;
        }
        return false;
    }

    /// <summary>
    /// スライドショーでの次のファイルパスを返す
    /// </summary>
    /// <returns>path</returns>
    /// <param name="step">1なら１つ次、-1なら１つ前</param>
    private string GetNextFile(int step) {
        List<string> files;
        int currentIndex = 0;

        if (targetFiles.Count > 0) {
            // 対象ファイルが指定されている場合はそのリストをたどる
            currentIndex = targetFiles.IndexOf(currentFile);
            files = targetFiles;
        } else {
            // 対象ファイル指定なしならば、現在のファイルと同じディレクトリから一覧を取得
            //   利便性のため、毎回一覧を取得
            string directory = Path.GetDirectoryName(currentFile);
            files = new List<string>();
            AddTargetDirectory(directory, ref files);   // ディレクトリ内の画像一覧を取得
            files.Sort();   // パスの順に並び替え
            currentIndex = files.IndexOf(currentFile);
            //Debug.Log("Index: " + currentIndex);
        }

        int index = currentIndex + step;
        if ((currentIndex >= (files.Count - 1)) && (step > 0))
        {
            // 最後のファイル表示中にさらに次を押されたら、最初に送る
            index = 0;
        }
        else if ((currentIndex == 0) && (step < 0))
        {
            // 最初のファイル表示中にさらに前を押されたら、最後に送る
            index = files.Count - 1;
        }

        if (index < 0)
        {
            // インデックスが0より小さくなったら、先頭とする
            index = 0;
        }
        else if (index >= files.Count) {
            // インデックスがリストを超えたら、最後に送る
            index = files.Count - 1;
        } 
        return files[index];
    }

    /// <summary>
    /// ダイアログからファイルを開く
    /// </summary>
    private void OpenFile()
    {
        // ロード中は不用意に操作されないようフラグを立てておく
        isLoading = true;

        // Standalone File Browserを利用
        var extensions = new[] {
                new ExtensionFilter("Image Files",  imageExtensions),
                new ExtensionFilter("Movie Files", movieExtensions ),
                new ExtensionFilter("All Files", "*" ),
            };
        //string[] files = StandaloneFileBrowser.OpenFilePanel("Open File", "", extensions, false);
        StandaloneFileBrowser.OpenFilePanelAsync("Open image", "", extensions, false, OpenFileCallback);
    }

    /// <summary>
    /// 非同期ファイルダイアログの完了時コールバック
    /// </summary>
    /// <param name="files"></param>
    private void OpenFileCallback(string[] files)
    {
        if (files.Length < 1)
        {
            isLoading = false;
            return;
        }

        string path = files[0];
        if (!string.IsNullOrEmpty(path))
        {
            LoadFile(path);
        }
        else
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// ファイルがドロップされた時の処理
    /// </summary>
    /// <param name="files"></param>
    private void Window_OnFilesDropped(string[] files)
    {
        // 自分のウィンドウにフォーカスを与える
        window.Focus();

        // 表示対象リストを消去
        targetFiles.Clear();

        foreach (string path in files)
        {
            if (File.Exists(path))
            {
                // 画像ならば表示対象に追加
                if (CheckImageFile(path) || CheckMovieFile(path))
                {
                    targetFiles.Add(path);
                }
            }
            else if (Directory.Exists(path))
            {
                // フォルダならばその中の画像を表示対象に追加
                AddTargetDirectory(path, ref targetFiles);
            }
        }
        targetFiles.Sort();

        if (targetFiles.Count < 1) return;

        // 1ファイルだけ読み込み
        LoadFile(targetFiles[0]);

        // 指定ファイルが1つしかなければ、表示対象リストなしとして同一フォルダ内探索を行う。
        // そうでなければ表示対象のみのスライドショーとする
        if (targetFiles.Count == 1)
        {
            targetFiles.Clear();
        }
    }

    /// <summary>
    /// タイル数を推定
    /// プリセットにあるパターン（4x6,4x8,5x9,6x8）＋ 6x10 のどれかに限定
    /// </summary>
    /// <param name="texture"></param>
    /// <returns></returns>
    private Quilt.Settings GetTilingType(Texture2D texture)
    {
        List<Quilt.Settings> tilingPresets = new List<Quilt.Settings>();
        foreach (var preset in Quilt.presets)
        {
            if ((preset.quiltHeight == texture.height) && (preset.quiltWidth == texture.width))
            {
                // 画像サイズがプリセットのサイズと一致すれば候補とする
                tilingPresets.Add(preset);
            }
            else
            {
                // サイズが一致しなければ、そのtileX,tileYでサイズを合わせた候補を作成
                tilingPresets.Add(
                    new Quilt.Settings(
                        texture.width, texture.height,
                        preset.viewColumns, preset.viewRows,
                        preset.numViews
                        ));
            }
        }
        // これまであった 6x10 を追加
        tilingPresets.Add(
            new Quilt.Settings(texture.width, texture.height, 6, 10, 60)
            );

        // どれも候補に残らなければ初期指定のTilingにしておく
        if (tilingPresets.Count < 1)
        {
            return defaultTiling;
        }

        // テクスチャを配列に取得
        Color[] pixels = texture.GetPixels(0, 0, texture.width, texture.height);

        // この変数にTiling候補ごとの評価値（小さい方が良い）が入る
        float[] score = new float[tilingPresets.Count];

        // 相関をとる周期の調整値。1だと全ピクセルについて相関をとるが遅い。
        int skip = texture.width / 512;     // 固定値 4 としてもでも動いたが、それだと4096pxのとき遅い
        if (skip < 1) skip = 1;             // 最低1はないと無限ループとなってしまう

        // Tiling候補ごとに類似度を求める
        int index = 0;
        foreach (var preset in tilingPresets)
        {
            score[index] = 0;
            for (int v = 0; v < preset.viewHeight; v += skip)
            {
                for (int u = 0; u < preset.viewWidth; u += skip)
                {
                    //// まず、平均値を求める
                    //Color sum = Color.clear;
                    //for (int y = 0; y < preset.tilesY; y++)
                    //{
                    //    for (int x = 0; x < preset.tilesX; x++)
                    //    {
                    //        Color color = pixels[(y * preset.tileSizeY + v) * texture.width + (x * preset.tileSizeX + u)];
                    //        sum += color;
                    //    }
                    //}
                    //Color average = sum / preset.numViews;

                    // 中央タイルの画素を平均値の代わりに利用する
                    //   （各タイル間ではわずかな違いしかないという前提）
                    int centerTileY = preset.viewRows / 2;
                    int centerTileX = preset.viewColumns / 2;
                    Color average = pixels[(centerTileY * preset.viewHeight+ v) * texture.width + (centerTileX * preset.viewWidth + u)];

                    // 求めた平均を使い、分散を出す
                    Color variance = Color.clear;
                    for (int y = 0; y < preset.viewRows; y++)
                    {
                        for (int x = 0; x < preset.viewColumns; x++)
                        {
                            Color color = pixels[(y * preset.viewHeight+ v) * texture.width + (x * preset.viewWidth + u)];
                            Color diff = color - average;
                            variance += diff * diff;
                        }
                    }

                    // 分散の合計(SSD)を求める
                    score[index] += (variance.r + variance.g + variance.b);
                }
            }
            index++;
        }

        // 最も評価値が良かったTilingを選択
        int selectedIndex = 0;
        float minScore = float.MaxValue;
        for (int i = 0; i < tilingPresets.Count; i++)
        {
            //Debug.Log(tilingPresets[i].presetName + " : " + score[i]);

            if (minScore > score[i])
            {
                selectedIndex = i;
                minScore = score[i];
            }
        }
        return tilingPresets[selectedIndex];
    }
}
