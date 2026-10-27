using System;
using System.Collections.Generic;
using System.Text;

namespace MainGameBlankMapAdd.Media.WebCam
{
    internal static class WebCamDeviceCatalog
    {
        internal static string FormatReport(IEnumerable<string> deviceNames)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 利用可能なWebCamデバイス一覧");
            builder.AppendLine("# VideoPath に webcam://デバイス名 と入力してください");
            builder.AppendLine();
            bool found = false;
            foreach (string name in deviceNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                builder.AppendLine("webcam://" + name);
                found = true;
            }
            if (!found) builder.AppendLine("(デバイスなし)");
            return builder.ToString();
        }
    }
}
