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
                    string base64Data = EncodeSparkCode(rawTree);

                    // 获取本地化的武器名称
                    string weaponName = currentWeapon.Name.Localized();

                    // 组装最终的“星火分享码” (第一行是头+数据，第二行是玩家信息)
                    string weaponCode = $"SPT-ProjectSpark-WBM-{base64Data}\n玩家XXX分享了他的改枪码: {weaponName}";

                    // 写入剪贴板
                    GUIUtility.systemCopyBuffer = weaponCode;
                    Console.WriteLine($"改枪码导出成功! 代码:{weaponCode}");
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
                List<RawWeaponNode> importedTree = DecodeSparkCode(clipboardText);
                try
                {
                    //importedTree = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RawWeaponNode>>(clipboardText);
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
                            Console.WriteLine($"[造物失败] 无法在当前客户端找到物品 TPL: {node.tpl} (可能是第三方Mod物品或已被废弃)");
                        }
                    }

                    // 如果存在任何缺失的物品，执行熔断！
                    if (missingItemsTpl.Count > 0)
                    {
                        Console.WriteLine($"[致命拦截] 发现 {missingItemsTpl.Count} 个缺失物品，导入已终止。");

                        // 调用塔科夫原生的右下角弹窗系统，弹出红色错误提示
                        NotificationManagerClass.DisplayWarningNotification(
                            $"星火计划导入失败！\n此分享码包含了您当前游戏中不存在的物品 (可能需要特定的 Mod)。\n缺失数量: {missingItemsTpl.Count}"
                        );
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
                                        Console.WriteLine($"[拼装警告] 强制安装 {childItem.Name.Localized()} 到槽位 {actualSlotIndex} 失败: {addResult.Error}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[致命越界] 物品 {parentItem.Name.Localized()} 根本没有第 {actualSlotIndex} 个槽位！");
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

                        string presetName = "⭐星火计划导入预设";
                        WeaponBuildClass fakeBuild = new WeaponBuildClass(new MongoID(Guid.NewGuid().ToString("N").Substring(0, 24)), presetName, presetName, finalWeapon, false);

                        MethodInfo method31 = AccessTools.Method(typeof(EditBuildScreen), "method_31");
                        if (method31 != null)
                        {
                            method31.Invoke(__instance, new object[] { fakeBuild });
                            Console.WriteLine("====== [星火计划] 导入大获成功！ ======");

                            // 导入成功时，弹出一个绿色的成功通知
                            NotificationManagerClass.DisplayMessageNotification("星火计划改枪码导入成功！");
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
                    NotificationManagerClass.DisplayWarningNotification("星火计划遇到内部错误，导入失败，请查看控制台日志。");
                }
            });

            var impUpdater = importButton.gameObject.AddComponent<SparkButtonUpdater>();
            impUpdater.ScreenInstance = __instance;
            impUpdater.TargetText = "导入星火码"; // 锁定文字
            if (importButton.gameObject.GetComponent<CanvasGroup>() == null) importButton.gameObject.AddComponent<CanvasGroup>();
        }
        // 1. 先写一个辅助方法：把 24位字符串转为 12字节数组
        public static byte[] HexStringToBytes(string hex)
        {
            byte[] bytes = new byte[12];
            for (int i = 0; i < 12; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // 2. 将你的 rawTree 压成 Base64 星火码
        public static string EncodeSparkCode(List<RawWeaponNode> rawTree)
        {
            // 用 MemoryStream 和 BinaryWriter 高效写内存
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                // 遍历你的 20 个节点
                for (int i = 0; i < rawTree.Count; i++)
                {
                    var node = rawTree[i];

                    // 1. 写入 12 字节的 TPL
                    writer.Write(HexStringToBytes(node.tpl));

                    // 2. 写入 1 字节的 自身 Index (由于你原来的逻辑里节点就是按顺序加进 List 的，直接用 i 即可)
                    writer.Write((byte)i);

                    // 3. 写入 1 字节的 Parent Index
                    // 如果 parent 为空，写 255；否则去树里找那个 parent 的 index
                    if (string.IsNullOrEmpty(node.parent))
                    {
                        writer.Write((byte)255);
                    }
                    else
                    {
                        // 找出父节点在列表里的下标
                        int parentIndex = rawTree.FindIndex(n => n.id == node.parent);
                        writer.Write((byte)parentIndex);
                    }

                    // 4. 写入 1 字节的 Slot Index
                    // 同样，如果是根节点，写 255
                    if (string.IsNullOrEmpty(node.slotId)) // 或者判断 slotIndex == 0
                    {
                        writer.Write((byte)255);
                    }
                    else
                    {
                        // 记得你导出时是从 1 开始的，如果想要 0-based，这里可以减 1，
                        // 或者直接存进去，导入时减 1，全看你的喜好
                        writer.Write((byte)node.slotIndex);
                    }
                }

                // 全部写完后，把整个内存流转换成 Base64 字符串
                return Convert.ToBase64String(ms.ToArray());
            }
        }
        // 1. 辅助方法：把 12 字节数组还原回 24位 Hex 字符串 (TPL)
        public static string BytesToHexString(byte[] bytes)
        {
            // BitConverter 会把数组变成 "AA-BB-CC..." 的形式，我们去掉横杠并转小写
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        // 2. 核心解码方法：Base64 -> 节点树
        public static List<RawWeaponNode> DecodeSparkCode(string clipboardText)
        {
            List<RawWeaponNode> rawTree = new List<RawWeaponNode>();

            try
            {
                string text = clipboardText.Trim();
                string base64Data = text;

                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    string firstLine = lines[0];
                    string prefix = "SPT-ProjectSpark-WBM-";

                    // 2. 掐掉文件头
                    if (firstLine.StartsWith(prefix))
                    {
                        base64Data = firstLine.Substring(prefix.Length);
                    }
                    else
                    {
                        // 极佳的鲁棒性：如果玩家只复制了中间的乱码（没有头），我们也放行
                        base64Data = firstLine;
                    }
                }

                // 将 Base64 乱码还原为真实的内存字节流
                byte[] data = Convert.FromBase64String(base64Data);

                // 【防呆护盾】：15 字节校验！如果不是 15 的倍数，说明代码被篡改或复制不全
                if (data.Length % 15 != 0)
                {
                    Console.WriteLine($"[星火计划-解码致命错误] 数据长度异常 ({data.Length} bytes)，非完整 15 字节区块！");
                    return null;
                }

                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    int nodeCount = data.Length / 15;

                    // 精准切割每一个 15 字节的节点区块
                    for (int i = 0; i < nodeCount; i++)
                    {
                        // 1. 提取前 12 字节 -> 还原为 TPL
                        byte[] tplBytes = reader.ReadBytes(12);
                        string tpl = BytesToHexString(tplBytes);

                        // 2. 提取后 3 字节 -> 还原关系网络
                        byte selfIndex = reader.ReadByte();
                        byte parentIndex = reader.ReadByte();
                        byte slotIndex = reader.ReadByte();

                        // 3. 重新组装成我们熟悉的 RawWeaponNode
                        RawWeaponNode node = new RawWeaponNode();
                        node.tpl = tpl;

                        // 我们直接用 SelfIndex 的字符串形式作为临时关联 ID (完美对接 memoryItems)
                        node.id = selfIndex.ToString();

                        // 如果父级是 255 (0xFF)，说明是武器本体，没有爹
                        node.parent = parentIndex == 255 ? "" : parentIndex.ToString();

                        // 如果槽位是 255，说明是武器本体，槽位记为 0
                        node.slotIndex = slotIndex == 255 ? 0 : slotIndex;

                        rawTree.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[星火计划-解码崩溃] Base64 解析失败，可能复制了非法的字符串。错误: {ex.Message}");
                return null;
            }

            return rawTree;
        }
    }
}