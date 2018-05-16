// Copyright(c) 2018 Shingo Mori
// Released under the MIT license
// http://opensource.org/licenses/mit-license.php

using HoloToolkit.Unity.InputModule;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.VR.WSA.WebCam;

public class GetFaceInfo : MonoBehaviour, IInputClickHandler
{
    // UI周りのフィールド
    public GameObject UiImagePrefab; //顔ごとに生成するUI部品のプレファブ
    public GameObject Canvas;        //UIを配置するためのメインのキャンバス
    public Text SystemMessage;       //状況表示用メッセージエリア
    public RawImage photoPanel;      //debug用のキャプチャ表示パネル

    // カメラ周りのパラメータ
    private Resolution cameraResolution;
    private PhotoCapture photoCaptureObject = null;
    private Quaternion cameraRotation;

    // Azure側のパラメータ群
    private string personGroupId = "YOUR_GROUP_ID"; //FaceAPIで設定したpersonGroupIdをセットする
    private string FaceAPIKey    = "YOUR_APP_KEY";  //FaceAPIのAPPキーをセットする
    private string region        = "YOUR_REGION";   //FaceAPIの地域をセットする
    private string DetectURL;
    private string IdentifyURL;
    private string GetPersonURL;

    public void OnInputClicked(InputClickedEventData eventData)
    {
        AnalyzeScene();
    }

    void Start()
    {
        InputManager.Instance.AddGlobalListener(gameObject);

        DetectURL    = "https://" + region + ".api.cognitive.microsoft.com/face/v1.0/detect";
        GetPersonURL = "https://" + region + ".api.cognitive.microsoft.com/face/v1.0/persongroups/" + personGroupId + "/persons/";
        IdentifyURL  = "https://" + region + ".api.cognitive.microsoft.com/face/v1.0/identify";

        cameraResolution = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).First();
    }

    private void AnalyzeScene()
    {
        DisplaySystemMessage("Detect Start...");
        PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
    }

    //PhotoCaptureの取得は下記参照
    //https://docs.microsoft.com/ja-jp/windows/mixed-reality/locatable-camera-in-unity
    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        DisplaySystemMessage("Take Picture...");
        photoCaptureObject = captureObject;

        CameraParameters c = new CameraParameters();
        c.hologramOpacity = 0.0f;
        c.cameraResolutionWidth = cameraResolution.width;
        c.cameraResolutionHeight = cameraResolution.height;
        c.pixelFormat = CapturePixelFormat.JPEG;

        captureObject.StartPhotoModeAsync(c, OnPhotoModeStarted);
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (result.success)
        {
            photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
        }
        else
        {
            Debug.LogError("Unable to start photo mode!");
        }
    }

    private void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame photoCaptureFrame)
    {
        if (result.success)
        {
            // Face APIに送るimageBufferListにメモリ上の画像をコピーする
            List<byte> imageBufferList = new List<byte>();
            photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBufferList);

            //ここはデバッグ用 送信画像の出力。どんな画像が取れたのか確認したい場合に使用。邪魔ならphotoPanelごと消してもよい。
            Texture2D debugTexture = new Texture2D(100, 100);
            debugTexture.LoadImage(imageBufferList.ToArray());
            photoPanel.texture = debugTexture;

            // カメラの向きをワールド座標に変換するためのパラメータ保持
            var cameraToWorldMatrix = new Matrix4x4();
            photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
            cameraRotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));


            Matrix4x4 projectionMatrix;
            photoCaptureFrame.TryGetProjectionMatrix(Camera.main.nearClipPlane, Camera.main.farClipPlane, out projectionMatrix);
            Matrix4x4 pixelToCameraMatrix = projectionMatrix.inverse;

            StartCoroutine(PostToFaceAPI(imageBufferList.ToArray(), cameraToWorldMatrix, pixelToCameraMatrix));
        }
        photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
    }

    private void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        photoCaptureObject.Dispose();
        photoCaptureObject = null;
    }

    /*
     * 取得した画像をFaceAPIに送信し、顔を検出する
     */
    private IEnumerator<object> PostToFaceAPI(byte[] imageData, Matrix4x4 cameraToWorldMatrix, Matrix4x4 pixelToCameraMatrix)
    {
        DisplaySystemMessage("Call Face API...");
        var headers = new Dictionary<string, string>() {
            { "Ocp-Apim-Subscription-Key", FaceAPIKey },
            { "Content-Type", "application/octet-stream" }
        };

        WWW www = new WWW(DetectURL, imageData, headers);
        yield return www;
        string responseString = www.text;

        JSONObject json = new JSONObject(responseString); //JSONObjectライブラリはUnityアセットストアより別途ダウンロードが必要

        var node = json.list.FirstOrDefault();
        if (node != null)
        {
            DisplaySystemMessage("Face detect finished...");
            CreateFaceUI(json, cameraRotation, cameraToWorldMatrix, pixelToCameraMatrix);
        }
        else
        {
            DisplaySystemMessage("No Face detected...");
        }
    }

    private void CreateFaceUI(JSONObject json, Quaternion rotation, Matrix4x4 cameraToWorldMatrix, Matrix4x4 pixelToCameraMatrix)
    {
        // 古いUIの削除
        DestroyOldUI();

        Dictionary<string, GameObject> faceUImap = new Dictionary<string, GameObject>();
        //検出した顔の数だけ繰り返す
        foreach (var result in json.list)
        {
            //顔の枠の生成
            var rect = result.GetField("faceRectangle");
            float top = -(rect.GetField("top").f / cameraResolution.height - .5f);
            float left = rect.GetField("left").f / cameraResolution.width - .5f;
            float width = rect.GetField("width").f / cameraResolution.width;
            float height = rect.GetField("height").f / cameraResolution.height;

            // テキストエリアの生成とサイズ、向きの調整
            GameObject uiImageObject = (GameObject)Instantiate(UiImagePrefab);
            GameObject rectImageObject = uiImageObject.transform.Find("Image").gameObject;

            Vector3 txtOrigin = cameraToWorldMatrix.MultiplyPoint3x4(pixelToCameraMatrix.MultiplyPoint3x4(new Vector3(left + left, top + top, 0)));
            uiImageObject.transform.position = txtOrigin;
            uiImageObject.transform.rotation = rotation;
            uiImageObject.transform.Rotate(new Vector3(0, 1, 0), 180);
            uiImageObject.tag = "faceText";
            uiImageObject.transform.parent = Canvas.transform;

            RectTransform faceDetectedImageRectTransform = rectImageObject.GetComponent<RectTransform>();
            faceDetectedImageRectTransform.sizeDelta = new Vector2(width, height);

            // 生成したUIオブジェクトをFaceIDと紐づけて格納
            faceUImap.Add(result.GetField("faceId").str, uiImageObject);
        }

        // すべての顔のUI部品生成が終わったらIdentifyを呼び出す
        StartCoroutine(IdentifyByFaceId(faceUImap));
    }

    private void DestroyOldUI()
    {
        // 前回生成したUIの削除
        var existing = GameObject.FindGameObjectsWithTag("faceText");
        List<string> FaceIdList = new List<string>();
        foreach (var go in existing)
        {
            Destroy(go);
        }
    }

    /*
     * 取得した FaceId を使って、Person Group に登録されている顔情報を検索（Identify）する。
     * 顔情報が検出されると PersonId が取得できる。
     * 
     * PersonId取得後、PersonId を使って、ユーザ情報を取得する。
     */
    IEnumerator IdentifyByFaceId(Dictionary<string, GameObject> faceUImap)
    {
        DisplaySystemMessage("Start Identify...");

        //FaceIdの取得
        var header = new Dictionary<string, string>() {
            { "Content-Type", "application/json" },
            { "Ocp-Apim-Subscription-Key", FaceAPIKey }
        };

        // サーバへPOSTするデータを設定 
        PersonId id = new PersonId();
        id.personGroupId = this.personGroupId;

        //呼び出し回数節約のため一回で全部取得したいので、FaceIDをまとめて全部渡す
        id.faceIds = new string[faceUImap.Keys.Count];
        faceUImap.Keys.CopyTo(id.faceIds, 0);

        string json = JsonUtility.ToJson(id);
        byte[] bytes = Encoding.UTF8.GetBytes(json.ToString());

        WWW www = new WWW(IdentifyURL, bytes, header);
        yield return www;

        JSONObject j = new JSONObject(www.text);

        // faceIdの数だけ繰り返す
        foreach (var result in j.list)
        {

            string faceId = result.GetField("faceId").str;

            GameObject uiObject = faceUImap[faceId];
            Text txtArea = uiObject.GetComponentInChildren<Text>();
            var candidates = result.GetField("candidates");
            string personId = "";
            float confidence = 0.0f;

            if (candidates.list.Count > 0)
            {
                //候補者はconfidenceの高い順に格納されているので、1件目を採用する。
                personId = candidates.list[0].GetField("personId").str;
                confidence = candidates.list[0].GetField("confidence").f;
            }
            else
            {
                // personIdが取得できなかった（人物特定失敗）はSkip
                DisplaySystemMessage("Not registered...");
                txtArea.text = "Not registered Person";
                continue;
            }

            //ユーザ情報の取得
            StartCoroutine(GetPersonInfo(personId, confidence, txtArea, uiObject.GetComponentInChildren<RawImage>()));
        }

        DisplaySystemMessage("Done...");
    }


    /*
     * PersonIDを元にユーザー情報を取得する。
     * ユーザー情報（userData）は所属,肩書,URLのカンマ区切りで設定している前提。
     * 例：
     *   "name" : "Shingo Mori",
     *   "userData" : "TIS Inc,Section Chief,https://testBlob/image.jpg"
     */
    private IEnumerator GetPersonInfo(string personId, float confidence, Text txtArea, RawImage panel)
    {
        //ユーザ情報の取得
        UnityWebRequest request = UnityWebRequest.Get(GetPersonURL + personId);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Ocp-Apim-Subscription-Key", FaceAPIKey);

        yield return request.Send();

        JSONObject req = new JSONObject(request.downloadHandler.text);
        string name = req.GetField("name").str;
        string userData = req.GetField("userData").str;

        // テキストエリアの編集
        string message = name;
        message += System.Environment.NewLine;
        string[] data = userData.Split(',');
        message += data[0] + System.Environment.NewLine;
        message += data[1] + System.Environment.NewLine;
        message += "Identify Confidence:" + confidence * 100 + "%";
        txtArea.text = message;

        // イメージ画像取得用URLを移送
        string imageUrl = data[2];

        //顔写真の取得
        StartCoroutine(GetFaceImage(panel, imageUrl));
    }

    /*
     * 画像ファイルをダウンロードし、テクスチャに貼り付ける
     */
    private IEnumerator GetFaceImage(RawImage imagePanel, string targetUrl)
    {
        WWW www = new WWW(targetUrl);
        // 画像ダウンロード完了を待機
        yield return www;

        // 画像をパネルにセット
        imagePanel.texture = www.textureNonReadable;
    }

    /*
     * 状況出力用メッセージ
     */
    private void DisplaySystemMessage(string message)
    {
        SystemMessage.text = message;
    }

    /*
     * Face API呼び出し用のクラス
     */
    [Serializable]
    public class PersonId
    {
        public string personGroupId;
        public string[] faceIds;
    }
}