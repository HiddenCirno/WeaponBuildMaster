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
        //dll目录
        public static string dllPath = Assembly.GetExecutingAssembly().Location;
        public static string pluginDir = Path.GetDirectoryName(dllPath);
        private void Awake()
        {
            Logger.LogInfo("塔科夫改枪王正在加载...");
            //加载Patch
            new EditBuildScreenShowPatch().Enable();
            //加载配置
            LocaleManager.Init(Config);
            Logger.LogInfo("界面注入补丁已生效！");
        }
    }
}
