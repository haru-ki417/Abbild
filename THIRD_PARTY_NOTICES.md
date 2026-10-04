# 使っているソフトウェア・フォント

| 名前 | 用途 | ライセンス |
| --- | --- | --- |
| [MonoGame](https://github.com/MonoGame/MonoGame) 3.8.5 | ゲームの土台（描画・入力・音） | Microsoft Public License (MS-PL) |
| [SDL2](https://www.libsdl.org/)（MonoGame に同梱） | ウィンドウ・入力 | zlib License |
| [OpenAL Soft](https://github.com/kcat/openal-soft)（MonoGame に同梱、`openal.dll`） | 音の再生 | GNU LGPL 2.0 以降（動的リンク。差しかえ可能な DLL として同梱） |
| [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) | 文字の描画 | zlib License |
| [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp) | フォントの読み込み | MIT / Unlicense |
| [NVorbis](https://github.com/NVorbis/NVorbis) | BGM（Ogg Vorbis）の読み出し（`Content/bgm.dat` から） | MIT License |
| System.IO.Ports / .NET ランタイム | シリアル通信・実行環境 | MIT License |
| [DotGothic16](https://github.com/fontworks-fonts/DotGothic16) | フォント | SIL Open Font License 1.1（`Content/fonts/OFL.txt`） |

ブラウザー版（`src/Abbild.Web`）で使っているもの:

| 名前 | 用途 | ライセンス |
| --- | --- | --- |
| [KNI](https://github.com/kniEngine/kni) 4.3（MonoGame 互換、WebGL・WebAudio） | ブラウザーでの描画・入力・音。`wwwroot/js/streamProcessor2.js`・`micProcessor.js` は KNI のテンプレートから | Microsoft Public License (MS-PL) |
| [StbImageSharp](https://github.com/StbSharp/StbImageSharp)（KNI に同梱） | 画像の読み込み | MIT / Unlicense |
| [FontStashSharp.Kni](https://github.com/FontStashSharp/FontStashSharp) | 文字の描画 | zlib License |
| ASP.NET Core Blazor WebAssembly / .NET ランタイム | ブラウザーでの実行環境 | MIT License |

素材（画像・BGM）については [docs/ASSETS.md](docs/ASSETS.md) を参照してください。
