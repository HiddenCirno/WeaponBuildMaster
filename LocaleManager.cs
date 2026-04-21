using BepInEx.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeaponBuildMaster
{
    public static class LocaleManager
    {
        public static ConfigEntry<string> CurrentLanguage;

        // 核心翻译字典：[语言名称 (如"简体中文") -> [Key -> 翻译文本]]
        private static readonly Dictionary<string, Dictionary<string, string>> _loadedTranslations = new Dictionary<string, Dictionary<string, string>>();

        // 默认的回退语言名称（必须和 JSON 里的 "Language" 字段一致）
        private const string FallbackLangName = "English";

        public static void Init(ConfigFile config)
        {
            string dirPath = Path.Combine(PluginsCore.pluginDir, "locales");
            //if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            _loadedTranslations.Clear();
            List<string> availableLanguages = new List<string>();

            // 1. 遍历目录下所有的 json 文件 (不在乎文件名是什么)
            string[] jsonFiles = Directory.GetFiles(dirPath, "*.json");
            foreach (string file in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    LocaleData data = JsonConvert.DeserializeObject<LocaleData>(json);

                    if (data != null && !string.IsNullOrEmpty(data.Language) && data.Translate != null)
                    {
                        // 2. 将读取到的语言名称和翻译字典存入内存
                        _loadedTranslations[data.Language] = data.Translate;
                        availableLanguages.Add(data.Language);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[EFTBallisticCalculator] UI Locale Load Error ({file}): {e.Message}");
                }
            }

            // 防呆：如果没有读到任何文件，给一个兜底选项
            if (availableLanguages.Count == 0)
            {
                availableLanguages.Add(FallbackLangName);
                _loadedTranslations[FallbackLangName] = new Dictionary<string, string>();
            }

            // 3. 动态生成 F12 的配置项（下拉菜单完全由读取到的语言名称构成！）
            CurrentLanguage = config.Bind(
                "Language / 语言",
                "HUD Language / HUD 界面语言",
                availableLanguages.Contains(FallbackLangName) ? FallbackLangName : availableLanguages[0],
                new ConfigDescription(
                    "Change HUD UI's display language (Applies immediately). / 更改游戏内 HUD 界面的显示语言（即时生效）。",
                    new AcceptableValueList<string>(availableLanguages.ToArray()) // <--- 动态下拉框
                ));
        }

        public static string Get(string key)
        {
            // 1. 尝试从当前选择的语言中获取
            if (_loadedTranslations.TryGetValue(CurrentLanguage.Value, out var currentDict))
            {
                if (currentDict.TryGetValue(key, out var text)) return text;
            }

            // 2. 如果没找到，尝试从回退语言（英文）中获取
            if (_loadedTranslations.TryGetValue(FallbackLangName, out var fallbackDict))
            {
                if (fallbackDict.TryGetValue(key, out var fallbackText)) return fallbackText;
            }

            // 3. 终极防呆，返回 Key 原文
            return key;
        }
    }
}
