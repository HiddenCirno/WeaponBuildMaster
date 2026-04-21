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
            if (_buttonText != null && !string.IsNullOrEmpty(TargetText) && _buttonText.text != TargetText)
            {
                _buttonText.text = TargetText;
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
            var originalButtonField = AccessTools.Field(typeof(EditBuildScreen), "_saveAsBuildButton");
            Button originalButton = originalButtonField.GetValue(__instance) as Button;

            if (originalButton == null) return;

            Transform parent = originalButton.transform.parent;

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
            Button exportButton = UnityEngine.Object.Instantiate(originalButton);
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

                    Console.WriteLine("====== [星火计划] JSON 导出成功并已复制到剪贴板 ======\n" + jsonExport);
                }
                else
                {
                    Console.WriteLine("当前工作台上没有有效的武器！");
                }
            });

            var expUpdater = exportButton.gameObject.AddComponent<SparkButtonUpdater>();
            expUpdater.ScreenInstance = __instance;
            expUpdater.TargetText = "导出星火码"; // 锁定文字
            if (exportButton.gameObject.GetComponent<CanvasGroup>() == null) exportButton.gameObject.AddComponent<CanvasGroup>();

            // ==========================================
            // 2. 创建【导入按钮】
            // ==========================================
            Button importButton = UnityEngine.Object.Instantiate(originalButton);
            importButton.name = "SparkImportButton";
            importButton.transform.SetParent(parent, false);
            importButton.gameObject.SetActive(true);

            RectTransform impRect = importButton.GetComponent<RectTransform>();
            impRect.anchoredPosition3D += new Vector3(-300f, 0f, 0f); // 往左多挪 150 像素

            importButton.onClick.RemoveAllListeners();
            importButton.onClick.AddListener(() =>
            {
                Console.WriteLine("====== [星火计划] 尝试导入数据 ======");

                // 第一关：尝试获取剪贴板
                string clipboardText = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    Console.WriteLine("[拦截] 剪贴板为空！");
                    return;
                }

                // 第二关：尝试反序列化 (防呆，防止玩家乱贴别的文本)
                List<RawWeaponNode> importedTree = null;
                try
                {
                    importedTree = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RawWeaponNode>>(clipboardText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[拦截] 反序列化失败，剪贴板内容不是有效的预设 JSON。错误信息: {ex.Message}");
                    return;
                }

                if (importedTree == null || importedTree.Count == 0)
                {
                    Console.WriteLine("[拦截] 导入的预设数据为空！");
                    return;
                }

                // 第三关：获取当前工作台基底
                Item currentWeapon = (Item)AccessTools.Field(typeof(EditBuildScreen), "Item").GetValue(__instance);
                if (currentWeapon == null)
                {
                    Console.WriteLine("[拦截] 当前工作台上没有武器，无法作为基底导入！");
                    return;
                }

                // 第四关：基底 tpl 严格校验！防止 AK 导进 M4
                if (importedTree[0].tpl != currentWeapon.TemplateId)
                {
                    Console.WriteLine($"[致命拦截] 基底不匹配！当前武器的 TPL: {currentWeapon.TemplateId}，预设基底的 TPL: {importedTree[0].tpl}");
                    // TODO: 可以在这里调用塔科夫自带的 NotificationManagerClass.DisplayWarningNotification() 弹黄字警告玩家
                    return;
                }

                Console.WriteLine("[通行] 前置校验全部通过！准备进行虚空造物...");
                // TODO: 下一步，应用数据逻辑！
                try
                {
                    ItemFactoryClass itemFactory = Comfort.Common.Singleton<ItemFactoryClass>.Instance;

                    // 1. 字典缓存：记录 JSON 里的老 ID 对应我们在内存里新造出来的 Item
                    Dictionary<string, Item> memoryItems = new Dictionary<string, Item>();
                    Item newRootWeapon = null;

                    // 2. 遍历 JSON 列表，先把所有的零件“无中生有”地造出来
                    foreach (var node in importedTree)
                    {
                        // 为了防止导入的预设 ID 和玩家仓库里的物品冲突，我们生成全新的 24 位 Hex 字符串作为 MongoID
                        string freshId = Guid.NewGuid().ToString("N").Substring(0, 24);

                        // 塔科夫标准造物 API：CreateItem
                        Item newItem = itemFactory.CreateItem(new MongoID(freshId), node.tpl, null);

                        if (newItem != null)
                        {
                            memoryItems[node.id] = newItem;

                            // 如果这是没有 parent 的根节点，标记为武器本体
                            if (string.IsNullOrEmpty(node.parent))
                            {
                                newRootWeapon = newItem;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[造物失败] 无法识别的物品 TPL: {node.tpl}");
                            return; // 如果造不出某个核心零件，直接终止导入
                        }
                    }

                    // 3. 按照 parent 和 slotIndex，把散落的零件像乐高一样拼起来
                    foreach (var node in importedTree)
                    {
                        // 根节点（或 index 为 0）不需要找爸爸
                        if (string.IsNullOrEmpty(node.parent) || node.slotIndex == 0) continue;

                        Item childItem = memoryItems[node.id];

                        // 找到它的父节点
                        if (memoryItems.TryGetValue(node.parent, out Item parentItem))
                        {
                            if (parentItem is CompoundItem compoundParent)
                            {
                                // 【核心修改】：抛弃 slotId 字符串，直接用 Index 计算真实数组下标！
                                // 导出时加了 1，导入时减掉 1
                                int actualSlotIndex = node.slotIndex - 1;

                                // 【防呆护盾】：严格检查数组越界！
                                // 如果未来塔科夫更新删除了某个槽位，直接拦截，防止 IndexOutOfRangeException 搞崩游戏
                                if (actualSlotIndex >= 0 && actualSlotIndex < compoundParent.Slots.Length)
                                {
                                    // $O(1)$ 极速定位槽位！
                                    Slot targetSlot = compoundParent.Slots[actualSlotIndex];

                                    var addResult = targetSlot.AddWithoutRestrictions(childItem);
                                    if (addResult.Failed)
                                    {
                                        Console.WriteLine($"[拼装警告] 强制安装 {childItem.Name.Localized()} 到槽位 {actualSlotIndex} 失败: {addResult.Error}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[致命越界] 物品 {parentItem.Name.Localized()} 根本没有第 {actualSlotIndex} 个槽位！(总槽位数量: {compoundParent.Slots.Length})");
                                }
                            }
                        }
                    }

                    // ==========================================
                    // 第六关：提交给 UI 刷新
                    // ==========================================
                    if (newRootWeapon != null && newRootWeapon is Weapon finalWeapon)
                    {
                        Console.WriteLine("[星火计划] 树结构拼装完毕，正在推送到游戏 UI...");

                        // 伪造一个 WeaponBuildClass 骗过底层的 method_31
                        string presetName = "⭐星火计划导入预设";
                        WeaponBuildClass fakeBuild = new WeaponBuildClass(new MongoID(Guid.NewGuid().ToString("N").Substring(0, 24)), presetName, presetName, finalWeapon, false);

                        // 通过反射调用 method_31，触发整个界面的刷新链路
                        MethodInfo method31 = AccessTools.Method(typeof(EditBuildScreen), "method_31");
                        if (method31 != null)
                        {
                            method31.Invoke(__instance, new object[] { fakeBuild });
                            Console.WriteLine("====== [星火计划] 导入大获成功！ ======");
                        }
                        else
                        {
                            Console.WriteLine("[致命错误] 找不到刷新 UI 的 method_31！");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[星火计划-导入崩溃] {ex.Message}\n{ex.StackTrace}");
                }
            });

            var impUpdater = importButton.gameObject.AddComponent<SparkButtonUpdater>();
            impUpdater.ScreenInstance = __instance;
            impUpdater.TargetText = "导入星火码"; // 锁定文字
            if (importButton.gameObject.GetComponent<CanvasGroup>() == null) importButton.gameObject.AddComponent<CanvasGroup>();
        }

    }
}