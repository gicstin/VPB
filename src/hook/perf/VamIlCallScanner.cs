using System;
using System.Reflection;
using System.Reflection.Emit;

namespace VPB
{
    internal static class VamIlCallScanner
    {
        static OpCode[] s_OneByte;
        static OpCode[] s_TwoByte;
        static bool[] s_OneByteKnown;
        static bool[] s_TwoByteKnown;

        static void EnsureTables()
        {
            if (s_OneByte != null) return;
            var oneByte = new OpCode[256];
            var twoByte = new OpCode[256];
            var oneKnown = new bool[256];
            var twoKnown = new bool[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode)) continue;
                var op = (OpCode)field.GetValue(null);
                ushort value = unchecked((ushort)op.Value);
                if (op.Size == 1)
                {
                    oneByte[value & 0xFF] = op;
                    oneKnown[value & 0xFF] = true;
                }
                else if ((value >> 8) == 0xFE)
                {
                    twoByte[value & 0xFF] = op;
                    twoKnown[value & 0xFF] = true;
                }
            }
            s_OneByte = oneByte;
            s_TwoByte = twoByte;
            s_OneByteKnown = oneKnown;
            s_TwoByteKnown = twoKnown;
        }

        internal static bool TryScan(MethodBase method, byte[] il, Predicate<MethodBase> match, out bool matched)
        {
            matched = false;
            if (method == null || il == null || match == null) return false;
            try
            {
                EnsureTables();
                Module module = method.Module;
                Type declaring = method.DeclaringType;
                Type[] typeArgs = declaring != null && declaring.IsGenericType ? declaring.GetGenericArguments() : null;
                Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

                int pos = 0;
                while (pos < il.Length)
                {
                    OpCode op;
                    byte b = il[pos++];
                    if (b == 0xFE)
                    {
                        if (pos >= il.Length) return false;
                        byte second = il[pos++];
                        if (!s_TwoByteKnown[second]) return false;
                        op = s_TwoByte[second];
                    }
                    else
                    {
                        if (!s_OneByteKnown[b]) return false;
                        op = s_OneByte[b];
                    }

                    int operandSize;
                    switch (op.OperandType)
                    {
                        case OperandType.InlineNone:
                            operandSize = 0;
                            break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar:
                            operandSize = 1;
                            break;
                        case OperandType.InlineVar:
                            operandSize = 2;
                            break;
                        case OperandType.InlineBrTarget:
                        case OperandType.InlineField:
                        case OperandType.InlineI:
                        case OperandType.InlineSig:
                        case OperandType.InlineString:
                        case OperandType.InlineType:
                        case OperandType.ShortInlineR:
                            operandSize = 4;
                            break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR:
                            operandSize = 8;
                            break;
                        case OperandType.InlineSwitch:
                            if (pos + 4 > il.Length) return false;
                            operandSize = 4 + 4 * BitConverter.ToInt32(il, pos);
                            break;
                        case OperandType.InlineMethod:
                        case OperandType.InlineTok:
                            if (pos + 4 > il.Length) return false;
                            MethodBase callee = ResolveMethodToken(module, BitConverter.ToInt32(il, pos), typeArgs, methodArgs, op.OperandType == OperandType.InlineMethod);
                            if (callee == null && op.OperandType == OperandType.InlineMethod) return false;
                            if (callee != null && match(callee))
                            {
                                matched = true;
                                return true;
                            }
                            operandSize = 4;
                            break;
                        default:
                            return false;
                    }
                    if (operandSize < 0 || pos + operandSize > il.Length) return false;
                    pos += operandSize;
                }
                return true;
            }
            catch
            {
                matched = false;
                return false;
            }
        }

        static MethodBase ResolveMethodToken(Module module, int token, Type[] typeArgs, Type[] methodArgs, bool mustBeMethod)
        {
            if (mustBeMethod) return module.ResolveMethod(token, typeArgs, methodArgs);
            try { return module.ResolveMember(token, typeArgs, methodArgs) as MethodBase; }
            catch { return null; }
        }
    }
}
