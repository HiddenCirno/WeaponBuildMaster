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
        public string TargetText; // 目标锁定文本
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

            // 文本防篡改（根据不同的按钮锁定不同的名字）
            if (_buttonText != null && !string.IsNullOrEmpty(TargetText) && _buttonText.text != LocaleManager.Get(TargetText))
            {
                _buttonText.text = LocaleManager.Get(TargetText);
            }

            // 暴力常亮
            if (_button != null) _button.interactable = true;
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
            }
        }
    }

    // --- 技术验证专用：纯字符串存储节点 ---
    public class RawWeaponNode
    {
        public string id;       // 唯一实例 ID
        public string tpl;      // 物品模板 ID
        public string parent;   // 父节点的唯一 ID (根节点为空)
        public string slotId;   // 所在的 Slot ID (根节点为空)
        public int slotIndex;   // 预留的顺序索引
    }

    public class EditBuildScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EditBuildScreen), nameof(EditBuildScreen.Show),
                new Type[] { typeof(Item), typeof(Item), typeof(InventoryController), typeof(ISession) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EditBuildScreen __instance)
        {
            var originalButtonSaveField = AccessTools.Field(typeof(EditBuildScreen), "_saveAsBuildButton");
            Button originalButtonSave = originalButtonSaveField.GetValue(__instance) as Button;
            var originalButtonOpenField = AccessTools.Field(typeof(EditBuildScreen), "_openBuildButton"); //_openBuildButton
            Button originalButtonOpen = originalButtonOpenField.GetValue(__instance) as Button;


            if (originalButtonSave == null || originalButtonOpen == null) return;

            Transform parent = originalButtonSave.transform.parent;

            // 检查我们的按钮是不是已经存在了
            Transform existingExport = parent.Find("SparkExportButton");
            Transform existingImport = parent.Find("SparkImportButton");

            if (existingExport != null && existingImport != null)
            {
                existingExport.gameObject.SetActive(true);
                existingImport.gameObject.SetActive(true);
                return;
            }

            // ==========================================
            // 1. 创建【导出按钮】
            // ==========================================
            Button exportButton = UnityEngine.Object.Instantiate(originalButtonSave);
            exportButton.name = "SparkExportButton";
            exportButton.transform.SetParent(parent, false);
            exportButton.gameObject.SetActive(true);

            RectTransform expRect = exportButton.GetComponent<RectTransform>();
            expRect.anchoredPosition3D += new Vector3(-150f, 0f, 0f);

            exportButton.onClick.RemoveAllListeners();
            exportButton.onClick.AddListener(() =>
            {
                Item currentWeapon = (Item)AccessTools.Field(typeof(EditBuildScreen), "Item").GetValue(__instance);
                if (currentWeapon != null)
                {
                    List<RawWeaponNode> rawTree = new List<RawWeaponNode>();

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

                    TraverseRawTree(currentWeapon, "", "", 0);

                    string jsonExport = Newtonsoft.Json.JsonConvert.SerializeObject(rawTree, Newtonsoft.Json.Formatting.Indented);
                    GUIUtility.systemCopyBuffer = jsonExport; // 直接塞进剪贴板！

                    Console.WriteLine("====== [枪匠大师]: JSON 导出成功并已复制到剪贴板 ======\n");// + jsonExport);
                    string base64Data = PresetCodeUtils.EncodeSparkCode(rawTree);

                    // 获取本地化的武器名称
                    string weaponName = currentWeapon.Name.Localized();

                    //var profile = Traverse.Create(__instance).Property("Profile").GetValue<Profile>();
                    var profile = AccessTools.Field(typeof(EditBuildScreen), "profile_0").GetValue(__instance) as Profile;
                    string playerName = profile?.Nickname ?? LocaleManager.Get("wbm_default_player_name");//"神秘的PMC";

                    // 组装最终的“星火分享码” (第一行是头+数据，第二行是玩家信息)
                    string weaponCode = $"SPT-ProjectSpark-WBM-{base64Data}\n{string.Format(LocaleManager.Get("wbm_shared_code_text"), playerName)}: {weaponName}";

                    // 写入剪贴板
                    GUIUtility.systemCopyBuffer = weaponCode;
                    Console.WriteLine($"[枪匠大师]: 改枪码导出成功! 代码:{weaponCode}");
                    PresetCodeUtils.ShowMessage(LocaleManager.Get("wbm_export_successful"));
                }
                else
                {
                    Console.WriteLine("[枪匠大师]: 当前工作台上没有有效的武器！");
                }
            });

            var expUpdater = exportButton.gameObject.AddComponent<SparkButtonUpdater>();
            expUpdater.ScreenInstance = __instance;
            expUpdater.TargetText = "wbm_export_button";//"导出星火码"; // 锁定文字
            if (exportButton.gameObject.GetComponent<CanvasGroup>() == null) exportButton.gameObject.AddComponent<CanvasGroup>();

            // ==========================================
            // 2. 创建【导入按钮】
            // ==========================================
            Button importButton = UnityEngine.Object.Instantiate(originalButtonOpen);
            importButton.name = "SparkImportButton";
            importButton.transform.SetParent(parent, false);
            importButton.gameObject.SetActive(true);

            RectTransform impRect = importButton.GetComponent<RectTransform>();
            impRect.anchoredPosition3D += new Vector3(-300f, 0f, 0f); // 往左多挪 150 像素

            importButton.onClick.RemoveAllListeners();
            importButton.onClick.AddListener(() =>
            {
                Console.WriteLine("====== [枪匠大师]: 尝试导入数据 ======");

                // 第一关：尝试获取剪贴板
                string clipboardText = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    Console.WriteLine("[枪匠大师]: 剪贴板为空！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_101"));
                    return;
                }

                // 第二关：尝试反序列化 (防呆，防止玩家乱贴别的文本)
                List<RawWeaponNode> importedTree = PresetCodeUtils.DecodeSparkCode(clipboardText);
                try
                {
                    //importedTree = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RawWeaponNode>>(clipboardText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[枪匠大师]: 反序列化失败，剪贴板内容不是有效的预设 JSON。错误信息: {ex.Message}");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_102"));
                    return;
                }

                if (importedTree == null || importedTree.Count == 0)
                {
                    Console.WriteLine("[枪匠大师]: 导入的预设数据为空！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_error_201"));
                    return;
                }

                // 第三关：获取当前工作台基底
                Item currentWeapon = (Item)AccessTools.Field(typeof(EditBuildScreen), "Item").GetValue(__instance);
                if (currentWeapon == null)
                {
                    Console.WriteLine("[枪匠大师]: 当前工作台上没有武器，无法作为基底导入！");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_import_warn_103"));
                    return;
                }

                // 第四关：基底 tpl 严格校验！防止 AK 导进 M4
                if (importedTree[0].tpl != currentWeapon.TemplateId)
                {
                    Console.WriteLine($"[枪匠大师]: 基底不匹配！当前武器的 TPL: {currentWeapon.TemplateId}，预设基底的 TPL: {importedTree[0].tpl}");
                    PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_error_warn_202"));
                    // TODO: 可以在这里调用塔科夫自带的 NotificationManagerClass.DisplayWarningNotification() 弹黄字警告玩家
                    return;
                }

                Console.WriteLine("[枪匠大师]: 前置校验全部通过！准备进行构造...");
                // TODO: 下一步，应用数据逻辑！
                // ==========================================
                // 第五关：虚空造物与拼装 (The Magic Happens Here)
                // ==========================================
                try
                {
                    ItemFactoryClass itemFactory = Comfort.Common.Singleton<ItemFactoryClass>.Instance;

                    Dictionary<string, Item> memoryItems = new Dictionary<string, Item>();
                    Item newRootWeapon = null;

                    // 记录缺失的物品 TPL，用于向玩家报错
                    List<string> missingItemsTpl = new List<string>();

                    // 2. 遍历 JSON 列表，先把所有的零件“无中生有”地造出来
                    foreach (var node in importedTree)
                    {
                        string freshId = Guid.NewGuid().ToString("N").Substring(0, 24);

                        // 尝试创建物品
                        Item newItem = null;
                        try
                        {
                            newItem = itemFactory.CreateItem(new MongoID(freshId), node.tpl, null);
                        }
                        catch
                        {
                            // 有些极端情况下 CreateItem 会直接抛异常而不是返回 null
                            newItem = null;
                        }

                        // 【空物品防御：严格熔断机制】
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
                            missingItemsTpl.Add(node.tpl);
                            Console.WriteLine($"[枪匠大师]: 构建失败, 无法在当前客户端找到物品 TPL: {node.tpl} (可能是第三方Mod物品或已被删除)");
                            PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_300"), node.tpl));
                        }
                    }

                    // 如果存在任何缺失的物品，执行熔断！
                    if (missingItemsTpl.Count > 0)
                    {
                        Console.WriteLine($"[枪匠大师]: 致命错误! 发现 {missingItemsTpl.Count} 个缺失物品，导入已终止。");

                        // 调用塔科夫原生的右下角弹窗系统，弹出红色错误提示

                        PresetCodeUtils.ShowErrorMessage(string.Format(LocaleManager.Get("wbm_error_warn_301"), missingItemsTpl.Count));
                        //NotificationManagerClass.DisplayWarningNotification(
                        //    $"改枪码导入失败！\n此分享码包含了您当前游戏中不存在的物品 (可能需要特定的 Mod)。\n缺失数量: {missingItemsTpl.Count}"
                        //);
                        return; // 彻底终止后续的组装和 UI 刷新
                    }

                    // 3. 按照 parent 和 slotIndex，把散落的零件像乐高一样拼起来
                    // ... (这部分代码保持你上一版优化后的 Index 寻址逻辑不变) ...
                    foreach (var node in importedTree)
                    {
                        if (string.IsNullOrEmpty(node.parent) || node.slotIndex == 0) continue;

                        // 由于前面有了严格的熔断机制，走到这里 memoryItems 绝对是完整无缺的
                        Item childItem = memoryItems[node.id];

                        if (memoryItems.TryGetValue(node.parent, out Item parentItem))
                        {
                            if (parentItem is CompoundItem compoundParent)
                            {
                                int actualSlotIndex = node.slotIndex - 1;

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

                    // ==========================================
                    // 第六关：提交给 UI 刷新
                    // ==========================================
                    if (newRootWeapon != null && newRootWeapon is Weapon finalWeapon)
                    {
                        Console.WriteLine("[枪匠大师]: 树结构拼装完毕，正在推送到游戏 UI...");

                        string presetName = "⭐改枪码导入预设";
                        WeaponBuildClass fakeBuild = new WeaponBuildClass(new MongoID(Guid.NewGuid().ToString("N").Substring(0, 24)), presetName, presetName, finalWeapon, false);

                        MethodInfo method31 = AccessTools.Method(typeof(EditBuildScreen), "method_31");
                        if (method31 != null)
                        {
                            method31.Invoke(__instance, new object[] { fakeBuild });
                            Console.WriteLine("====== [枪匠大师]: 导入成功！ ======");

                            // 导入成功时，弹出一个绿色的成功通知
                            PresetCodeUtils.ShowMessage(LocaleManager.Get("wbm_import_successful"));
                        }
                        else
                        {
                            Console.WriteLine("[枪匠大师]: 发生错误! 找不到刷新 UI 的 method_31！");
                            PresetCodeUtils.ShowErrorMessage(LocaleManager.Get("wbm_error_warn_500"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[枪匠大师]: 发生预料之外的错误! 错误信息: {ex.Message}\n{ex.StackTrace}");
                    NotificationManagerClass.DisplayWarningNotification(LocaleManager.Get("wbm_import_error_999"));
                }
            });

            var impUpdater = importButton.gameObject.AddComponent<SparkButtonUpdater>();
            impUpdater.ScreenInstance = __instance;
            impUpdater.TargetText = "wbm_import_button";//"导入星火码"; // 锁定文字
            if (importButton.gameObject.GetComponent<CanvasGroup>() == null) importButton.gameObject.AddComponent<CanvasGroup>();
        }
    }
}