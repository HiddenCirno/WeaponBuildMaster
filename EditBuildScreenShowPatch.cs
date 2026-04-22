using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using Fika.Core.Main.Utils;

namespace WeaponBuildMaster
{
    // 自定义的按钮状态更新器（支持设置目标文本以防止被游戏底层改名）
    public class SparkButtonUpdater : MonoBehaviour
    {
        public EditBuildScreen ScreenInstance;
        public string TargetText;
        private Button _button;
        private CanvasGroup _canvasGroup;
        private TextMeshProUGUI _buttonText;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _buttonText = GetComponentInChildren<TextMeshProUGUI>();
        }

        private void Update()
        {
            if (ScreenInstance == null) return;
            //更新文本
            if (_buttonText != null && !string.IsNullOrEmpty(TargetText) && _buttonText.text != LocaleManager.Get(TargetText))
            {
                _buttonText.text = LocaleManager.Get(TargetText);
            }
            //按钮常亮
            if (_button != null) _button.interactable = true;
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
            }
        }
    }
    //自定义武器树结构
    public class RawWeaponNode
    {
        public string id;       //唯一ID
        public string tpl;      //物品ID
        public string parent;   //父节点ID
        public string slotId;   //slotId
        public int slotIndex;   //slot索引
    }
    //Patch
    public class EditBuildScreenShowPatch : ModulePatch
    {
        //使用SPT封装的反射格式
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EditBuildScreen), nameof(EditBuildScreen.Show),
                new Type[] { typeof(Item), typeof(Item), typeof(InventoryController), typeof(ISession) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EditBuildScreen __instance)
        {
            //反射获取原按钮并复制
            var originalButtonSaveField = AccessTools.Field(typeof(EditBuildScreen), "_saveAsBuildButton");
            Button originalButtonSave = originalButtonSaveField.GetValue(__instance) as Button;
            var originalButtonOpenField = AccessTools.Field(typeof(EditBuildScreen), "_openBuildButton"); //_openBuildButton
            Button originalButtonOpen = originalButtonOpenField.GetValue(__instance) as Button;
            //防御
            if (originalButtonSave == null || originalButtonOpen == null) return;
            Transform parent = originalButtonSave.transform.parent;
            //检查按钮是不是已经存在
            Transform existingExport = parent.Find("SparkExportButton");
            Transform existingImport = parent.Find("SparkImportButton");
            if (existingExport != null && existingImport != null)
            {
                existingExport.gameObject.SetActive(true);
                existingImport.gameObject.SetActive(true);
                return;
            }
            //导出按钮
            Button exportButton = UnityEngine.Object.Instantiate(originalButtonSave);
            exportButton.name = "SparkExportButton";
            exportButton.transform.SetParent(parent, false);
            exportButton.gameObject.SetActive(true);
            RectTransform expRect = exportButton.GetComponent<RectTransform>();
            expRect.anchoredPosition3D += new Vector3(-150f, 0f, 0f);
            //清除事件
            exportButton.onClick.RemoveAllListeners();
            //添加自定义事件
            exportButton.onClick.AddListener(() =>
            {
                //反射获取当前武器
                Item currentWeapon = (Item)AccessTools.Field(typeof(EditBuildScreen), "Item").GetValue(__instance);
                if (currentWeapon != null)
                {
                    List<RawWeaponNode> rawTree = new List<RawWeaponNode>();
                    //递归提取数据生成自定义武器树结构
                    void TraverseRawTree(Item currentItem, string parentId, string slotId, int slotIndex)
                    {
                        rawTree.Add(new RawWeaponNode
                        {
                            id = currentItem.Id,
                            tpl = currentItem.TemplateId,
                            parent = parentId,
                            slotId = slotId,
                            slotIndex = slotIndex
                        });
                        if (currentItem is CompoundItem compoundItem)
                        {
                            for (var i = 0; i < compoundItem.Slots.Length; i++)
                            {
                                Slot slot = compoundItem.Slots[i];
                                if (slot.ContainedItem != null)
                                {
                                    TraverseRawTree(slot.ContainedItem, currentItem.Id, slot.ID, i + 1);
                                }
                            }
                        }
                    }
                    //生成树结构
                    TraverseRawTree(currentWeapon, "", "", 0);
                    //测试用
                    //string jsonExport = Newtonsoft.Json.JsonConvert.SerializeObject(rawTree, Newtonsoft.Json.Formatting.Indented);
                    //GUIUtility.systemCopyBuffer = jsonExport;
                    Console.WriteLine("====== [枪匠大师]: JSON 导出成功并已复制到剪贴板 ======\n");// + jsonExport);
                    //改枪码数据段计算
                    string base64Data = PresetCodeUtils.EncodeSparkCode(rawTree);
                    //武器名字
                    string weaponName = currentWeapon.Name.Localized();
                    //反射获取玩家名字
                    //var profile = Traverse.Create(__instance).Property("Profile").GetValue<Profile>();
                    var profile = AccessTools.Field(typeof(EditBuildScreen), "profile_0").GetValue(__instance) as Profile;
                    string playerName = profile?.Nickname ?? LocaleManager.Get("wbm_default_player_name");//"神秘的PMC";
                    //组合改枪码
                    string weaponCode = $"SPT-ProjectSpark-WBM-{base64Data}\n{string.Format(LocaleManager.Get("wbm_shared_code_text"), playerName)}: {weaponName}";
                    //写入剪贴板
                    GUIUtility.systemCopyBuffer = weaponCode;
                    Console.WriteLine($"[枪匠大师]: 改枪码导出成功! 代码:{weaponCode}");
                    PresetCodeUtils.ShowMessage(LocaleManager.Get("wbm_export_successful"));
                }
                else
                {
                    Console.WriteLine("[枪匠大师]: 当前工作台上没有有效的武器！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_103"));
                }
            });
            //更新组件
            var expUpdater = exportButton.gameObject.AddComponent<SparkButtonUpdater>();
            expUpdater.ScreenInstance = __instance;
            expUpdater.TargetText = "wbm_export_button";//"导出星火码"; // 锁定文字
            if (exportButton.gameObject.GetComponent<CanvasGroup>() == null) exportButton.gameObject.AddComponent<CanvasGroup>();

            //导入按钮
            Button importButton = UnityEngine.Object.Instantiate(originalButtonOpen);
            importButton.name = "SparkImportButton";
            importButton.transform.SetParent(parent, false);
            importButton.gameObject.SetActive(true);
            RectTransform impRect = importButton.GetComponent<RectTransform>();
            impRect.anchoredPosition3D += new Vector3(-300f, 0f, 0f);
            importButton.onClick.RemoveAllListeners();
            importButton.onClick.AddListener(() =>
            {
                Console.WriteLine("====== [枪匠大师]: 尝试导入数据 ======");
                //读取剪贴板
                string clipboardText = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    Console.WriteLine("[枪匠大师]: 剪贴板为空！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_101"));
                    return;
                }
                //预生成结构
                List<RawWeaponNode> importedTree = new List<RawWeaponNode>();
                try
                {
                    //反序列化当前内容
                    importedTree = PresetCodeUtils.DecodeSparkCode(clipboardText);
                    //importedTree = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RawWeaponNode>>(clipboardText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[枪匠大师]: 反序列化失败，剪贴板内容不是有效的预设 JSON。错误信息: {ex.Message}");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_102"));
                    return;
                }
                //预设无效
                if (importedTree == null || importedTree.Count == 0)
                {
                    Console.WriteLine("[枪匠大师]: 导入的预设数据为空！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_error_201"));
                    return;
                }
                //检查当前武器
                Item currentWeapon = (Item)AccessTools.Field(typeof(EditBuildScreen), "Item").GetValue(__instance);
                if (currentWeapon == null)
                {
                    Console.WriteLine("[枪匠大师]: 当前工作台上没有武器，无法作为基底导入！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_103"));
                    return;
                }
                //校验
                var weaponTemplate = importedTree[0].tpl;
                if (weaponTemplate != currentWeapon.TemplateId)
                {
                    Console.WriteLine($"[枪匠大师]: 基底不匹配！当前武器的 TPL: {currentWeapon.TemplateId}，预设基底的 TPL: {weaponTemplate}");
                    PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_202"), weaponTemplate));
                    return;
                }
                //校验通过
                Console.WriteLine("[枪匠大师]: 前置校验全部通过！准备进行构造...");
                //构造预设
                try
                {
                    //物品构造器实例, 单例
                    ItemFactoryClass itemFactory = Comfort.Common.Singleton<ItemFactoryClass>.Instance;
                    //存储节点
                    Dictionary<string, Item> memoryItems = new Dictionary<string, Item>();
                    Item newRootWeapon = null;
                    //缺失的物品
                    List<string> missingItemsTpl = new List<string>();
                    //遍历武器树, 生成节点
                    foreach (var node in importedTree)
                    {
                        string freshId = Guid.NewGuid().ToString("N").Substring(0, 24);
                        //构造物品节点
                        Item newItem = null;
                        try
                        {
                            newItem = itemFactory.CreateItem(new MongoID(freshId), node.tpl, null);
                        }
                        catch
                        {
                            //防空
                            newItem = null;
                        }
                        //空物品防御
                        if (newItem != null)
                        {
                            memoryItems[node.id] = newItem;

                            if (string.IsNullOrEmpty(node.parent))
                            {
                                newRootWeapon = newItem;
                            }
                        }
                        else
                        {
                            //捕获
                            missingItemsTpl.Add(node.tpl);
                            Console.WriteLine($"[枪匠大师]: 构建失败, 无法在当前客户端找到物品 TPL: {node.tpl} (可能是第三方Mod物品或已被删除)");
                            PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_300"), node.tpl));
                        }
                    }
                    //中断导入
                    if (missingItemsTpl.Count > 0)
                    {
                        Console.WriteLine($"[枪匠大师]: 致命错误! 发现 {missingItemsTpl.Count} 个缺失物品，导入已终止。");
                        PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_301"), missingItemsTpl.Count));
                        return; // 彻底终止后续的组装和 UI 刷新
                    }
                    //组合预设
                    foreach (var node in importedTree)
                    {
                        if (string.IsNullOrEmpty(node.parent) || node.slotIndex == 0) continue;
                        //从节点序列化预设
                        Item childItem = memoryItems[node.id];
                        if (memoryItems.TryGetValue(node.parent, out Item parentItem))
                        {
                            if (parentItem is CompoundItem compoundParent)
                            {
                                int actualSlotIndex = node.slotIndex - 1;
                                //强制安装配件
                                if (actualSlotIndex >= 0 && actualSlotIndex < compoundParent.Slots.Length)
                                {
                                    Slot targetSlot = compoundParent.Slots[actualSlotIndex];
                                    var addResult = targetSlot.AddWithoutRestrictions(childItem);

                                    if (addResult.Failed)
                                    {
                                        Console.WriteLine($"[枪匠大师]: 强制安装 {childItem.Name.Localized()} 到槽位 {actualSlotIndex} 失败: {addResult.Error}");
                                        PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_401"), childItem.Name.Localized(), actualSlotIndex));
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[枪匠大师]: 物品 {parentItem.Name.Localized()} 根本没有第 {actualSlotIndex} 个槽位！");
                                    PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_402"), childItem.Name.Localized(), actualSlotIndex));
                                }
                            }
                        }
                    }
                    //更新物品预览
                    if (newRootWeapon != null && newRootWeapon is Weapon finalWeapon)
                    {
                        Console.WriteLine("[枪匠大师]: 树结构拼装完毕，正在推送到游戏 UI...");
                        //内存使用的预设名
                        string presetName = "⭐改枪码导入预设";
                        WeaponBuildClass fakeBuild = new WeaponBuildClass(new MongoID(Guid.NewGuid().ToString("N").Substring(0, 24)), presetName, presetName, finalWeapon, false);
                        //反射调用方法, 更新显示
                        MethodInfo method31 = AccessTools.Method(typeof(EditBuildScreen), "method_31");
                        if (method31 != null)
                        {
                            method31.Invoke(__instance, new object[] { fakeBuild });
                            Console.WriteLine("====== [枪匠大师]: 导入成功！ ======");
                            //导入成功
                            PresetCodeUtils.ShowMessage(LocaleManager.Get("wbm_import_successful"));
                        }
                        else
                        {
                            //反射失败
                            Console.WriteLine("[枪匠大师]: 发生错误! 找不到刷新 UI 的 method_31！");
                            PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_error_warn_500"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    //导入过程发生未知错误, 捕获并返回错误码999
                    Console.WriteLine($"[枪匠大师]: 发生预料之外的错误! 错误信息: {ex.Message}\n{ex.StackTrace}");
                    NotificationManagerClass.DisplayWarningNotification(LocaleManager.Get("wbm_import_error_999"));
                }
            });
            //更新组件
            var impUpdater = importButton.gameObject.AddComponent<SparkButtonUpdater>();
            impUpdater.ScreenInstance = __instance;
            impUpdater.TargetText = "wbm_import_button";//"导入星火码"; // 锁定文字
            if (importButton.gameObject.GetComponent<CanvasGroup>() == null) importButton.gameObject.AddComponent<CanvasGroup>();
        }
    }
}