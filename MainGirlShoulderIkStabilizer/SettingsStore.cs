using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGirlShoulderIkStabilizer;

internal static class SettingsStore
{
	internal const string FileName = "ShoulderIkStabilizerSettings.json";

	private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	internal static PluginSettings LoadOrCreate(string pluginDir, Action<string> logInfo, Action<string> logWarn, Action<string> logError)
	{
		string path = Path.Combine(pluginDir, "ShoulderIkStabilizerSettings.json");
		try
		{
			if (!File.Exists(path))
			{
				PluginSettings created = Normalize(new PluginSettings());
				SaveInternal(path, created, backup: false, logWarn);
				logInfo?.Invoke("設定ファイルを新規作成: " + path);
				return created;
			}
			PluginSettings parsed = JsonUtility.FromJson<PluginSettings>(File.ReadAllText(path, Encoding.UTF8));
			if (parsed == null)
			{
				logWarn?.Invoke("設定ファイル解析に失敗。既定値を使用");
				parsed = new PluginSettings();
			}
			parsed = Normalize(parsed);
			SaveInternal(path, parsed, backup: false, logWarn);
			return parsed;
		}
		catch (Exception ex)
		{
			logError?.Invoke("設定ファイル読込に失敗: " + ex.Message);
			return Normalize(new PluginSettings());
		}
	}

	internal static void Save(string pluginDir, PluginSettings settings, Action<string> logWarn)
	{
		SaveInternal(Path.Combine(pluginDir, "ShoulderIkStabilizerSettings.json"), Normalize(settings), backup: false, logWarn);
	}

	private static void SaveInternal(string path, PluginSettings settings, bool backup, Action<string> logWarn)
	{
		string tempPath = path + ".tmp";
		try
		{
			string json = JsonUtility.ToJson(settings ?? new PluginSettings(), prettyPrint: true);
			json = NormalizeJsonNumericLiterals(json);
			File.WriteAllText(tempPath, json + Environment.NewLine, Utf8NoBom);
			if (backup && File.Exists(path))
			{
				CreateBackup(path, logWarn);
			}
			if (File.Exists(path))
			{
				File.Replace(tempPath, path, null);
			}
			else
			{
				File.Move(tempPath, path);
			}
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
	}

	private static string NormalizeJsonNumericLiterals(string json)
	{
		if (string.IsNullOrEmpty(json))
		{
			return json;
		}

		var sb = new StringBuilder(json.Length);
		bool inString = false;
		bool escaping = false;
		int i = 0;

		while (i < json.Length)
		{
			char ch = json[i];

			if (inString)
			{
				sb.Append(ch);
				if (escaping)
				{
					escaping = false;
				}
				else if (ch == '\\')
				{
					escaping = true;
				}
				else if (ch == '"')
				{
					inString = false;
				}

				i++;
				continue;
			}

			if (ch == '"')
			{
				inString = true;
				sb.Append(ch);
				i++;
				continue;
			}

			if (!IsNumberTokenStart(ch))
			{
				sb.Append(ch);
				i++;
				continue;
			}

			int start = i;
			int end = i;

			if (json[end] == '-')
			{
				end++;
			}

			bool hasDigits = false;
			while (end < json.Length && char.IsDigit(json[end]))
			{
				hasDigits = true;
				end++;
			}

			bool hasFraction = false;
			if (end < json.Length && json[end] == '.')
			{
				hasFraction = true;
				end++;
				while (end < json.Length && char.IsDigit(json[end]))
				{
					hasDigits = true;
					end++;
				}
			}

			bool hasExponent = false;
			if (end < json.Length && (json[end] == 'e' || json[end] == 'E'))
			{
				hasExponent = true;
				end++;
				if (end < json.Length && (json[end] == '+' || json[end] == '-'))
				{
					end++;
				}

				int expStart = end;
				while (end < json.Length && char.IsDigit(json[end]))
				{
					end++;
				}

				if (end == expStart)
				{
					hasExponent = false;
				}
			}

			string token = json.Substring(start, end - start);
			if (hasDigits && (hasFraction || hasExponent)
				&& double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
				&& !double.IsNaN(parsed) && !double.IsInfinity(parsed))
			{
				double rounded = Math.Round(parsed, 2, MidpointRounding.AwayFromZero);
				if (Math.Abs(rounded) < 0.005d)
				{
					rounded = 0d;
				}

				sb.Append(rounded.ToString("0.##", CultureInfo.InvariantCulture));
			}
			else
			{
				sb.Append(token);
			}

			i = end;
		}

		return sb.ToString();
	}

	private static bool IsNumberTokenStart(char ch)
	{
		return ch == '-' || char.IsDigit(ch);
	}

	private static PluginSettings Normalize(PluginSettings settings)
	{
		if (settings == null)
		{
			settings = new PluginSettings();
		}
		settings.ShoulderWeight = ClampFinite(settings.ShoulderWeight, 0f, 5f, 1.5f);
		settings.ShoulderOffset = ClampFinite(settings.ShoulderOffset, -1f, 1f, 0.2f);
		settings.ShoulderRightWeight = ClampFinite(settings.ShoulderRightWeight, 0f, 5f, settings.ShoulderWeight);
		settings.ShoulderRightOffset = ClampFinite(settings.ShoulderRightOffset, -1f, 1f, settings.ShoulderOffset);
		settings.LoweredArmScale = ClampFinite(settings.LoweredArmScale, 0f, 1f, 0.67f);
		settings.RaisedArmStartY = ClampFinite(settings.RaisedArmStartY, -0.1f, 0.5f, 0.03f);
		settings.RaisedArmFullY = ClampFinite(settings.RaisedArmFullY, -0.05f, 0.8f, 0.22f);
		if (settings.RaisedArmFullY < settings.RaisedArmStartY + 0.001f)
		{
			settings.RaisedArmFullY = settings.RaisedArmStartY + 0.001f;
		}
		settings.RaisedArmScaleMin = ClampFinite(settings.RaisedArmScaleMin, 0f, 1f, 0.25f);
		settings.MaxShoulderDeltaAngleDeg = ClampFinite(settings.MaxShoulderDeltaAngleDeg, 0f, 180f, 35f);
		settings.MaxSolverBlend = ClampFinite(settings.MaxSolverBlend, 0f, 1f, 0.8f);
		settings.ShoulderDiagnosticLogInterval = ClampFinite(settings.ShoulderDiagnosticLogInterval, 0.05f, 2f, 0.2f);
		return settings;
	}

	private static float ClampFinite(float value, float min, float max, float fallback)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			return fallback;
		}
		if (value < min)
		{
			return min;
		}
		if (value > max)
		{
			return max;
		}
		return value;
	}

	private static void CreateBackup(string path, Action<string> logWarn)
	{
		try
		{
			string backupPath = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
			File.Copy(path, backupPath, overwrite: true);
		}
		catch (Exception ex)
		{
			logWarn?.Invoke("設定バックアップに失敗: " + ex.Message);
		}
	}
}
