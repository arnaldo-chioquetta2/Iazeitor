using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Windows.Forms;

namespace atcIA
{

    public class INI
    {
        public string FileName;

        #region Constructors
            public INI(string filename)
            {
                FileName = filename;
            }
            public INI()
            {
                FileName = Path.ChangeExtension(Application.ExecutablePath, ".ini");
            }
        #endregion

        #region Read functions
            public string ReadString(string section, string ident, string defvalue)
            {
                StringBuilder sb = new StringBuilder(1024);
                GetPrivateProfileString(section, ident, defvalue, sb, 1024, FileName);
                return sb.ToString();
            }
            public int ReadInt(string section, string ident, int defvalue)
            {
                string erg = ReadString(section, ident, "");
                try
                {
                    return int.Parse(erg);
                }
                catch
                {
                    return defvalue;
                }
            }
            public bool ReadBool(string section, string ident, bool defvalue)
            {
                string erg = ReadString(section, ident, "");
                try
                {
                    return bool.Parse(erg);
                }
                catch
                {
                    return defvalue;
                }
            }
        #endregion

        #region Write functions
            public void WriteString(string section, string ident, string value)
            {
                if (!WritePrivateProfileString(section, ident, value, FileName))
                    throw new Exception("Error writing in " + FileName);
            }
            public void WriteBool(string section, string ident, bool value)
            {
                WriteString(section, ident, value.ToString());
            }
            public void WriteInt(string section, string ident, int value)
            {
                WriteString(section, ident, value.ToString());
            }
            public void WriteFloat(string section, string ident, float value)
            {
                WriteString(section, ident, value.ToString());
            }
            public void WriteDouble(string section, string ident, double value)
            {
                WriteString(section, ident, value.ToString());
            }
            public void WriteDecimal(string section, string ident, decimal value)
            {
                WriteString(section, ident, value.ToString());
            }

            public float ReadFloat(string section, string ident, float defvalue)
            {
                string erg = ReadString(section, ident, "");
                try
                {
                    return float.Parse(erg);
                }
                catch
                {
                    return defvalue;
                }
            }
            public double ReadDouble(string section, string ident, double defvalue)
            {
                string erg = ReadString(section, ident, "");
                try
                {
                    return double.Parse(erg);
                }
                catch
                {
                    return defvalue;
                }
            }
            public decimal ReadDecimal(string section, string ident, decimal defvalue)
            {
                string erg = ReadString(section, ident, "");
                try
                {
                    return decimal.Parse(erg);
                }
                catch
                {
                    return defvalue;
                }
            }
        #endregion

        #region InterOp
            [DllImport("Kernel32.dll")]
            private static extern bool WritePrivateProfileString(string Section, string key,
                    string value, string filename);
            [DllImport("Kernel32.dll")]
            private static extern UInt32 GetPrivateProfileString(string Section, string key,
                    string defvalue, StringBuilder buffer,
                    UInt32 bufsize, string filename);
        #endregion
    }

}
