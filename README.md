# HoloLensFaceAPISample

de:code 2018 AC62「簡単！！HoloLensで始めるCognitive Services～de:code 2018特別バージョン～」の  
Face API用サンプルコードです。  
HoloLensで画像キャプチャを取得し、FaceAPIを呼び出すことで\
該当のユーザー情報を取得します。  
また、あらかじめ設定しておいた画像URLを元にイメージ画像を取得し、  
ユーザー情報と共に画面表示します。  

## バージョン情報
 Unity：2017.1.2p3  
 MRToolkit：HoloToolkit-Unity-v1.2017.1.2.  
 VisualStudio：15.5.4  

## 使い方

1.本PJをクローンし、Azure Face APIのキー、グループID、regionを 
 GetFaceInfo.cs に設定してください。  

2.Face APIに登録する際のuserDataは下記のカンマ区切りでの定義としています。  
 所属,肩書,イメージ画像取得用URL  

3.エアタップで画像取得～FaceAPIの呼び出し～画像データの取得と表示までを行います。  

## 注意点

1.AzureおよびFace API自体の操作、設定に関しては本PJ内では説明致しません。

2.UWP Capability SettingsのWebcam,Internet Clientは必須です

3.本コード内でJSON処理のためにJSONObjectというライブラリを使用しています。  
 こちらは別途DLのうえAsset直下への配置が必要です。

 [Unityアセットストア](https://assetstore.unity.com/packages/tools/input-management/json-object-710)  
 [Github](https://github.com/mtschoen/JSONObject)

## 問い合わせ
twitter [@morio36](https://twitter.com/morio36)
