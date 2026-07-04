using System.Collections.Generic;

namespace GptBolDll
{
    public class ChatMessage
    {
        public string Role { get; set; } // user | model | system
        public string Content { get; set; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

}
