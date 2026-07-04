using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GptBolDll
{
    public static class gen
    {

        private static List<string> logMessages = new List<string>();
        public static string logFilePath = "";

        public static void Log(string mensagem)
        {
            logMessages.Add(mensagem);
            Console.WriteLine(mensagem);
            File.AppendAllLines(logFilePath, new[] { mensagem });
        }
    }
}
