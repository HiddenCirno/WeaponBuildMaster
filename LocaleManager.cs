using BepInEx.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeaponBuildMaster
{
    //本地化管理器
    public static class LocaleManager
    {
        //配置入口
        //当前选择的语言, 默认为英语, 默认自带中英双语
        public static ConfigEntry<string> CurrentLanguage;
        //存储载入的本地化字典
        private static readonly Dictionary<string, Dictionary<string, string>> _loadedTranslations = new Dictionary<string, Dictionary<string, string>>();
        //fallback的语言
        private const string FallbackLangName = "English";
        //配置初始化
        public static void Init(ConfigFile config)
        {
            string dirPath = Path.Combine(PluginsCore.pluginDir, "locales");
            _loadedTranslations.Clear();
            List<string> availableLanguages = new List<string>();
            //遍历文件
            string[] jsonFiles = Directory.GetFiles(dirPath, "*.json");
            foreach (string file in jsonFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    LocaleData data = JsonConvert.DeserializeObject<LocaleData>(json);

                    if (data != null && !string.IsNullOrEmpty(data.Language) && data.Translate != null)
                    {
                        //加载
                        _loadedTranslations[data.Language] = data.Translate;
                        availableLanguages.Add(data.Language);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[EFTBallisticCalculator] UI Locale Load Error ({file}): {e.Message}");
                }
            }
            //空字典防御
            if (availableLanguages.Count == 0)
            {
                availableLanguages.Add(FallbackLangName);
                _loadedTranslations[FallbackLangName] = new Dictionary<string, string>();
            }
            //生成配置项
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
            //读取当前语言
            if (_loadedTranslations.TryGetValue(CurrentLanguage.Value, out var currentDict))
            {
                if (currentDict.TryGetValue(key, out var text)) return text;
            }
            //fallback
            if (_loadedTranslations.TryGetValue(FallbackLangName, out var fallbackDict))
            {
                if (fallbackDict.TryGetValue(key, out var fallbackText)) return fallbackText;
            }
            //fallback都读不到, 返回key
            return key;
        }
    }
}
