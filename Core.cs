using BepInEx;
using EFTBallisticCalculator;
using System;

namespace WeaponBuildMaster
{
    [BepInPlugin(PluginsInfo.GUID, PluginsInfo.NAME, PluginsInfo.VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("星火计划改枪码 (WeaponBuildMaster) 正在加载...");

            // 激活我们的界面补丁
            new EditBuildScreenShowPatch().Enable();

            Logger.LogInfo("界面注入补丁已生效！");
        }
    }
}
