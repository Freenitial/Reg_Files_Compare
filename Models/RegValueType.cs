// Standard Windows registry value types as serialized in .reg files.

namespace RegCompare.Models;

/// <summary>
/// Subset of the Windows registry value types that the .reg textual format can carry.
/// Inferred from the leading token of the raw value string (see <see cref="Services.RegTypeFormatter"/>).
/// </summary>
public enum RegValueType
{
    RegSz,
    RegExpandSz,
    RegMultiSz,
    RegDword,
    RegQword,
    RegBinary,
}
