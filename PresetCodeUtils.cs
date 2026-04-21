using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeaponBuildMaster
{
    //核心工具, 包含编码解码和提示器封装
    public class PresetCodeUtils
    {
        //物品Id编码方法
        //忽视MongoId的编码规则, 只看结果, 它是一串24位的16进制字符串
        //因此我们将其切割为12个16进制数字, 视作字节处理
        //12个字节用base64得到16个字符
        public static byte[] HexStringToBytes(string hex)
        {
            byte[] bytes = new byte[12];
            for (int i = 0; i < 12; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        //生成数据段
        public static string EncodeSparkCode(List<RawWeaponNode> rawTree)
        {
            //高速读写
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                //遍历树节点
                for (int i = 0; i < rawTree.Count; i++)
                {
                    var node = rawTree[i];
                    //写入物品段
                    writer.Write(HexStringToBytes(node.tpl));
                    //1字节存储节点索引
                    writer.Write((byte)i);
                    //1字节存储父节点索引
                    //根节点的父节点为空, 返回0xFF
                    if (string.IsNullOrEmpty(node.parent))
                    {
                        writer.Write((byte)255);
                    }
                    else
                    {
                        //父节点索引
                        int parentIndex = rawTree.FindIndex(n => n.id == node.parent);
                        writer.Write((byte)parentIndex);
                    }
                    //1字节存储slot索引
                    //依然根节点0xFF
                    if (string.IsNullOrEmpty(node.slotId)) // 或者判断 slotIndex == 0
                    {
                        writer.Write((byte)255);
                    }
                    else
                    {
                        //存储
                        writer.Write((byte)node.slotIndex);
                    }
                }
                //内存流转换到base64
                return Convert.ToBase64String(ms.ToArray());
            }
        }
        //物品Id解码
        public static string BytesToHexString(byte[] bytes)
        {
            //BitConverter会把数组变成AA-BB-CC的形式, 去掉横杠转小写
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
        //改枪码解码
        public static List<RawWeaponNode> DecodeSparkCode(string clipboardText)
        {
            //预生成树结构
            List<RawWeaponNode> rawTree = new List<RawWeaponNode>();
            try
            {
                //定义
                string text = clipboardText.Trim();
                string base64Data = text;
                //去除末尾换行符, 保留SPT-PS-WBM-数据段
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    string firstLine = lines[0];
                    string prefix = "SPT-ProjectSpark-WBM-";
                    //去除头
                    if (firstLine.StartsWith(prefix))
                    {
                        base64Data = firstLine.Substring(prefix.Length);
                    }
                    else
                    {
                        //仅保留改枪码也可以正常解析
                        base64Data = firstLine;
                    }
                }
                //还原字节流
                byte[] data = Convert.FromBase64String(base64Data);
                //数据段是完美的15个字节1个节点, 校验长度
                if (data.Length % 15 != 0)
                {
                    Console.WriteLine($"[枪匠大师]: 数据长度异常 ({data.Length} bytes)，非完整 15 字节区块！");
                    return null;
                }
                using (MemoryStream ms = new MemoryStream(data))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    int nodeCount = data.Length / 15;
                    //切割字段
                    for (int i = 0; i < nodeCount; i++)
                    {
                        //还原物品ID
                        byte[] tplBytes = reader.ReadBytes(12);
                        string tpl = BytesToHexString(tplBytes);
                        //还原节点信息
                        byte selfIndex = reader.ReadByte();
                        byte parentIndex = reader.ReadByte();
                        byte slotIndex = reader.ReadByte();
                        //重建树结构
                        RawWeaponNode node = new RawWeaponNode();
                        node.tpl = tpl;
                        node.id = selfIndex.ToString();
                        node.parent = parentIndex == 255 ? "" : parentIndex.ToString();
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
        //提示器封装
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
