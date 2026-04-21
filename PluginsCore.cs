using BepInEx;
using EFTBallisticCalculator;
using System;
using System.IO;
using System.Reflection;

namespace WeaponBuildMaster
{
    [BepInPlugin(PluginsInfo.GUID, PluginsInfo.NAME, PluginsInfo.VERSION)]
    public class PluginsCore : BaseUnityPlugin
    {
        public static string dllPath = Assembly.GetExecutingAssembly().Location;
        public static string pluginDir = Path.GetDirectoryName(dllPath);
        private void Awake()
        {
            Logger.LogInfo("星火计划改枪码 (WeaponBuildMaster) 正在加载...");

            // 激活我们的界面补丁
            new EditBuildScreenShowPatch().Enable();
            LocaleManager.Init(Config);

            Logger.LogInfo("界面注入补丁已生效！");
        }
    }
}
