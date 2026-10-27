using System.Globalization;
using System.Text;

namespace MainGameAgentCamera
{
    /// <summary>
    /// 応答JSONを自前で組み立てる。
    ///
    /// UnityEngine.JsonUtility は入れ子のクラスを黙って落とすことがあり、
    /// 実測でも camera / subject が丸ごと消えた。相手は外部のエージェントなので、
    /// Unity の serializer の機嫌に protocol を預けるわけにはいかない。
    ///
    /// 数値は必ず InvariantCulture で書く。日本語ロケールだと小数点が
    /// カンマになり、受け取り側の JSON パーサが壊れるため。
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(1024);
        private bool _needComma;

        internal JsonWriter()
        {
            _sb.Append('{');
        }

        internal JsonWriter Str(string key, string value)
        {
            Key(key);
            Escape(value ?? string.Empty);
            return this;
        }

        internal JsonWriter Bool(string key, bool value)
        {
            Key(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        internal JsonWriter Num(string key, float value)
        {
            Key(key);
            _sb.Append(Fmt(value));
            return this;
        }

        internal JsonWriter Vec(string key, UnityEngine.Vector3 v)
        {
            Key(key);
            _sb.Append('[').Append(Fmt(v.x)).Append(',').Append(Fmt(v.y)).Append(',').Append(Fmt(v.z)).Append(']');
            return this;
        }

        internal JsonWriter Strings(string key, string[] values)
        {
            Key(key);
            _sb.Append('[');
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) _sb.Append(',');
                    Escape(values[i] ?? string.Empty);
                }
            }
            _sb.Append(']');
            return this;
        }

        /// <summary>入れ子の開始。閉じるまでの間に書いた項目はこの中に入る。</summary>
        internal JsonWriter Begin(string key)
        {
            Key(key);
            _sb.Append('{');
            _needComma = false;
            return this;
        }

        internal JsonWriter End()
        {
            _sb.Append('}');
            _needComma = true;
            return this;
        }

        /// <summary>組み立て済みのJSON断片をそのまま差し込む。配列を別途作った時に使う。</summary>
        internal JsonWriter Raw(string key, string rawJson)
        {
            Key(key);
            _sb.Append(rawJson);
            return this;
        }

        internal string Finish()
        {
            _sb.Append('}');
            return _sb.ToString();
        }

        private void Key(string key)
        {
            if (_needComma) _sb.Append(',');
            _sb.Append('"').Append(key).Append("\":");
            _needComma = true;
        }

        private static string Fmt(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private void Escape(string value)
        {
            _sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
