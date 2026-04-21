using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeaponBuildMaster
{
    public class PresetCodeUtils
    {

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
                    Console.WriteLine($"[枪匠大师]: 数据长度异常 ({data.Length} bytes)，非完整 15 字节区块！");
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
                Console.WriteLine($"[枪匠大师]: Base64 解析失败，可能复制了非法的字符串。错误: {ex.Message}");
                return null;
            }

            return rawTree;
        }
        public static void ShowMessage(string message)
        {
            NotificationManagerClass.DisplayMessageNotification(message);
        }
        public static void ShowErrorMessage(string message)
        {
            NotificationManagerClass.DisplayWarningNotification(message);
        }
    }
}
