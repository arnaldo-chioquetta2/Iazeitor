using System.Collections.Generic;

namespace GptBolDll
{
    public class ChatHistory
    {
        private List<ChatMessage> messages = new List<ChatMessage>();

        public void AddUser(string text)
        {
            messages.Add(new ChatMessage("user", text));
        }

        public void AddModel(string text)
        {
            messages.Add(new ChatMessage("model", text));
        }

        public void AddSystem(string text)
        {
            messages.Add(new ChatMessage("system", text));
        }

        public List<ChatMessage> GetMessages()
        {
            return messages;
        }

        public void Clear()
        {
            messages.Clear();
        }
    }
}
