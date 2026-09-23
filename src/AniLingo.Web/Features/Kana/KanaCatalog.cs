using System.Security.Cryptography;
using System.Text;

namespace AniLingo.Web.Features.Kana;

public enum KanaScript
{
    Hiragana,
    Katakana
}

public sealed record KanaEntry(
    string Symbol,
    string Romaji,
    string Group,
    int Stage,
    KanaScript Script,
    bool IsCombination = false)
{
    public string ScriptKey => Script == KanaScript.Hiragana ? "hiragana" : "katakana";
}

public static class KanaCatalog
{
    public const string Language = "ja-kana";
    public const int MaxStage = 20;

    private static readonly (string Hiragana, string Katakana, string Romaji, string Group, int Stage)[] Rows =
    [
        ("あ","ア","a","Vokale",1), ("い","イ","i","Vokale",1), ("う","ウ","u","Vokale",1), ("え","エ","e","Vokale",1), ("お","オ","o","Vokale",1),
        ("か","カ","ka","K-Reihe",2), ("き","キ","ki","K-Reihe",2), ("く","ク","ku","K-Reihe",2), ("け","ケ","ke","K-Reihe",2), ("こ","コ","ko","K-Reihe",2),
        ("さ","サ","sa","S-Reihe",3), ("し","シ","shi","S-Reihe",3), ("す","ス","su","S-Reihe",3), ("せ","セ","se","S-Reihe",3), ("そ","ソ","so","S-Reihe",3),
        ("た","タ","ta","T-Reihe",4), ("ち","チ","chi","T-Reihe",4), ("つ","ツ","tsu","T-Reihe",4), ("て","テ","te","T-Reihe",4), ("と","ト","to","T-Reihe",4),
        ("な","ナ","na","N-Reihe",5), ("に","ニ","ni","N-Reihe",5), ("ぬ","ヌ","nu","N-Reihe",5), ("ね","ネ","ne","N-Reihe",5), ("の","ノ","no","N-Reihe",5),
        ("は","ハ","ha","H-Reihe",6), ("ひ","ヒ","hi","H-Reihe",6), ("ふ","フ","fu","H-Reihe",6), ("へ","ヘ","he","H-Reihe",6), ("ほ","ホ","ho","H-Reihe",6),
        ("ま","マ","ma","M-Reihe",7), ("み","ミ","mi","M-Reihe",7), ("む","ム","mu","M-Reihe",7), ("め","メ","me","M-Reihe",7), ("も","モ","mo","M-Reihe",7),
        ("や","ヤ","ya","Y-Reihe",8), ("ゆ","ユ","yu","Y-Reihe",8), ("よ","ヨ","yo","Y-Reihe",8),
        ("ら","ラ","ra","R-Reihe",9), ("り","リ","ri","R-Reihe",9), ("る","ル","ru","R-Reihe",9), ("れ","レ","re","R-Reihe",9), ("ろ","ロ","ro","R-Reihe",9),
        ("わ","ワ","wa","W + N",10), ("を","ヲ","o","W + N",10), ("ん","ン","n","W + N",10),
        ("が","ガ","ga","G-Reihe",11), ("ぎ","ギ","gi","G-Reihe",11), ("ぐ","グ","gu","G-Reihe",11), ("げ","ゲ","ge","G-Reihe",11), ("ご","ゴ","go","G-Reihe",11),
        ("ざ","ザ","za","Z-Reihe",12), ("じ","ジ","ji","Z-Reihe",12), ("ず","ズ","zu","Z-Reihe",12), ("ぜ","ゼ","ze","Z-Reihe",12), ("ぞ","ゾ","zo","Z-Reihe",12),
        ("だ","ダ","da","D-Reihe",13), ("ぢ","ヂ","ji","D-Reihe",13), ("づ","ヅ","zu","D-Reihe",13), ("で","デ","de","D-Reihe",13), ("ど","ド","do","D-Reihe",13),
        ("ば","バ","ba","B-Reihe",14), ("び","ビ","bi","B-Reihe",14), ("ぶ","ブ","bu","B-Reihe",14), ("べ","ベ","be","B-Reihe",14), ("ぼ","ボ","bo","B-Reihe",14),
        ("ぱ","パ","pa","P-Reihe",15), ("ぴ","ピ","pi","P-Reihe",15), ("ぷ","プ","pu","P-Reihe",15), ("ぺ","ペ","pe","P-Reihe",15), ("ぽ","ポ","po","P-Reihe",15),
        ("きゃ","キャ","kya","K/G-Kombinationen",16), ("きゅ","キュ","kyu","K/G-Kombinationen",16), ("きょ","キョ","kyo","K/G-Kombinationen",16),
        ("ぎゃ","ギャ","gya","K/G-Kombinationen",16), ("ぎゅ","ギュ","gyu","K/G-Kombinationen",16), ("ぎょ","ギョ","gyo","K/G-Kombinationen",16),
        ("しゃ","シャ","sha","S/Z-Kombinationen",17), ("しゅ","シュ","shu","S/Z-Kombinationen",17), ("しょ","ショ","sho","S/Z-Kombinationen",17),
        ("じゃ","ジャ","ja","S/Z-Kombinationen",17), ("じゅ","ジュ","ju","S/Z-Kombinationen",17), ("じょ","ジョ","jo","S/Z-Kombinationen",17),
        ("ちゃ","チャ","cha","T-Kombinationen",18), ("ちゅ","チュ","chu","T-Kombinationen",18), ("ちょ","チョ","cho","T-Kombinationen",18),
        ("にゃ","ニャ","nya","N/H/B/P-Kombinationen",19), ("にゅ","ニュ","nyu","N/H/B/P-Kombinationen",19), ("にょ","ニョ","nyo","N/H/B/P-Kombinationen",19),
        ("ひゃ","ヒャ","hya","N/H/B/P-Kombinationen",19), ("ひゅ","ヒュ","hyu","N/H/B/P-Kombinationen",19), ("ひょ","ヒョ","hyo","N/H/B/P-Kombinationen",19),
        ("びゃ","ビャ","bya","N/H/B/P-Kombinationen",19), ("びゅ","ビュ","byu","N/H/B/P-Kombinationen",19), ("びょ","ビョ","byo","N/H/B/P-Kombinationen",19),
        ("ぴゃ","ピャ","pya","N/H/B/P-Kombinationen",19), ("ぴゅ","ピュ","pyu","N/H/B/P-Kombinationen",19), ("ぴょ","ピョ","pyo","N/H/B/P-Kombinationen",19),
        ("みゃ","ミャ","mya","M/R-Kombinationen",20), ("みゅ","ミュ","myu","M/R-Kombinationen",20), ("みょ","ミョ","myo","M/R-Kombinationen",20),
        ("りゃ","リャ","rya","M/R-Kombinationen",20), ("りゅ","リュ","ryu","M/R-Kombinationen",20), ("りょ","リョ","ryo","M/R-Kombinationen",20)
    ];

    public static IReadOnlyList<KanaEntry> All { get; } = Rows
        .SelectMany(row => new[]
        {
            new KanaEntry(row.Hiragana, row.Romaji, row.Group, row.Stage, KanaScript.Hiragana, row.Stage >= 16),
            new KanaEntry(row.Katakana, row.Romaji, row.Group, row.Stage, KanaScript.Katakana, row.Stage >= 16)
        })
        .ToArray();

    public static IReadOnlyList<KanaEntry> ForStage(KanaScript script, int stage) =>
        All.Where(x => x.Script == script && x.Stage == Math.Clamp(stage, 1, MaxStage)).ToArray();

    public static KanaScript ParseScript(string? value) =>
        string.Equals(value, "katakana", StringComparison.OrdinalIgnoreCase)
            ? KanaScript.Katakana
            : KanaScript.Hiragana;

    public static string StageTitle(KanaScript script, int stage)
    {
        var group = ForStage(script, stage).FirstOrDefault()?.Group ?? "Kana";
        return (script == KanaScript.Hiragana ? "Hiragana" : "Katakana") + " · " + group;
    }

    public static Guid IdFor(KanaEntry entry)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Language + ":" + entry.Symbol));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static KanaEntry? Find(Guid termId) =>
        All.FirstOrDefault(entry => IdFor(entry) == termId);
}
