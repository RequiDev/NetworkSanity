using System;
using System.Runtime.InteropServices;
using UnhollowerBaseLib.Runtime;

namespace NetworkSanity.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Il2CppMethodInfo
    {
        public IntPtr methodPointer;
        public IntPtr invoker_method;
        public IntPtr name; // const char*
        public Il2CppClass* klass;
        public Il2CppTypeStruct* return_type;
        public Il2CppParameterInfo* parameters;

        public IntPtr someRtData;
        /*union
        {
            const Il2CppRGCTXData* rgctx_data; /* is_inflated is true and is_generic is false, i.e. a generic instance method #1#
            const Il2CppMethodDefinition* methodDefinition;
        };*/

        public IntPtr someGenericData;
        /*/* note, when is_generic == true and is_inflated == true the method represents an uninflated generic method on an inflated type. #1#
        union
        {
            const Il2CppGenericMethod* genericMethod; /* is_inflated is true #1#
            const Il2CppGenericContainer* genericContainer; /* is_inflated is false and is_generic is true #1#
        };*/

        public int customAttributeIndex;
        public uint token;
        public Il2CppMethodFlags flags;
        public Il2CppMethodImplFlags iflags;
        public ushort slot;
        public byte parameters_count;
        public MethodInfoExtraFlags extra_flags;
        /*uint8_t is_generic : 1; /* true if method is a generic method definition #1#
        uint8_t is_inflated : 1; /* true if declaring_type is a generic instance or if method is a generic instance#1#
        uint8_t wrapper_type : 1; /* always zero (MONO_WRAPPER_NONE) needed for the debugger #1#
        uint8_t is_marshaled_from_native : 1*/
    }
}
