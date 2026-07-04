using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace atcIA
{
    static class Program {
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            var Main = new Main();
            Application.Run(Main);
        }
    }
}
