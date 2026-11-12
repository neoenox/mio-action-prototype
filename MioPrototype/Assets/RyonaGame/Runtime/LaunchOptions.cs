using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ryona.Game
{
    /// <summary>
    /// 起動引数の読み取りを 1 か所にまとめる。
    ///
    /// 以前は各コンポーネントが <see cref="Environment.GetCommandLineArgs"/> を
    /// 個別に走査し、同じ解析コードを 30 か所に持っていた。ここへ寄せることで、
    ///
    /// ・<see cref="Has"/> … <c>-flag</c> が付いているか
    /// ・<see cref="Value"/> … <c>-key=value</c> / <c>-key value</c> の値
    /// ・<see cref="TryFloat"/> / <see cref="TryInt"/> … 数値として読む
    ///
    /// の 3 つだけ覚えればよくなる。
    ///
    /// 解析は最初に触られたとき 1 回だけ行う（<c>RuntimeInitializeOnLoadMethod</c> の
    /// 実行順に依存しない）。引数は起動中に変わらないので、結果はキャッシュしてよい。
    /// </summary>
    public static class LaunchOptions
    {
        static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static readonly HashSet<string> Switches =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static bool _parsed;

        /// <summary>
        /// 生の引数（Unity 自身の引数も含む）。特殊な形式を自前で見たいときだけ使う。
        /// </summary>
        public static string[] Args
        {
            get
            {
                EnsureParsed();
                return _args;
            }
        }

        static string[] _args;

        /// <summary><c>-flag</c>（値なし）が付いているか。</summary>
        public static bool Has(string flag)
        {
            EnsureParsed();
            return Switches.Contains(Key(flag));
        }

        /// <summary>
        /// 指定した接頭辞で始まる引数があるか（<c>-hold*</c> のような判定用）。
        /// 例: <c>HasPrefix("-hold")</c> は <c>-holdright</c> でも true。
        /// </summary>
        public static bool HasPrefix(string prefix)
        {
            EnsureParsed();
            string key = Key(prefix);

            foreach (string name in Switches)
            {
                if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// <c>-key=value</c> または <c>-key value</c> の値。無ければ null。
        /// </summary>
        public static string Value(string key)
        {
            EnsureParsed();
            return Values.TryGetValue(Key(key), out string value) ? value : null;
        }

        /// <summary>値を float として読む。付いていない・読めないときは false。</summary>
        public static bool TryFloat(string key, out float value)
        {
            value = 0f;
            string text = Value(key);
            return text != null &&
                   float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>値を int として読む。付いていない・読めないときは false。</summary>
        public static bool TryInt(string key, out int value)
        {
            value = 0;
            string text = Value(key);
            return text != null &&
                   int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        static string Key(string name) => name.Length > 0 && name[0] == '-' ? name.Substring(1) : name;

        static void EnsureParsed()
        {
            if (_parsed)
                return;

            _parsed = true;

            string[] args = Environment.GetCommandLineArgs();
            _args = args;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];

                // Unity 側の引数（-projectPath など）も同じ形なので、そのまま拾ってよい。
                if (a.Length < 2 || a[0] != '-')
                    continue;

                int equals = a.IndexOf('=');
                if (equals >= 0)
                {
                    string name = a.Substring(1, equals - 1);
                    Switches.Add(name);
                    Values[name] = a.Substring(equals + 1);
                    continue;
                }

                string flag = a.Substring(1);
                Switches.Add(flag);

                // `-key value` 形式。次のトークンが別のフラグでなければ値を拾う。
                if (i + 1 < args.Length && (args[i + 1].Length == 0 || args[i + 1][0] != '-'))
                    Values[flag] = args[i + 1];
            }
        }
    }
}
