using System;
using System.Xml;
using System.IO;

namespace GptBolDll
{
    public class GptbolParser
    {
        public XmlNode ParseConteudoGptbol(string conteudoGptbol)
        {
            // Cria um documento XML vazio para gerar nós manualmente
            XmlDocument doc = new XmlDocument();

            // Cria o nó raiz manualmente
            XmlElement root = doc.CreateElement("Gptbol");
            doc.AppendChild(root);

            using (StringReader reader = new StringReader(conteudoGptbol))
            {
                string line;
                XmlNode currentNode = root;

                // Analisa linha a linha
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    gen.Log($"Processando linha: {line} ");

                    if (line.Contains("</form>"))
                    {
                        int x = 0;
                    }

                    if (line.StartsWith("<") && line.EndsWith("/>"))
                    {
                        // Processa tags de abertura/fechamento automática, como <abr cam='...' />
                        var element = ProcessarTagSimples(doc, line);
                        currentNode.AppendChild(element);
                    }
                    else if (line.StartsWith("<") && line.EndsWith(">"))
                    {
                        // Processa tags de abertura, como <nav tp='bus' texto='...'>
                        var element = ProcessarTagComAtributos(doc, line);
                        currentNode.AppendChild(element);
                        currentNode = element;  // Define o nó atual como elemento aberto
                    }
                    else if (line.StartsWith("</"))
                    {
                        // Processa tags de fechamento, como </nav>
                        currentNode = currentNode.ParentNode;  // Retorna para o nó pai
                    }
                    else if (line.StartsWith("<![CDATA[") && line.EndsWith("]]>"))
                    {
                        // Processa conteúdo CDATA como um nó de texto
                        string cdataContent = line.Substring(9, line.Length - 12); // Remove <![CDATA[ e ]]>
                        var cdataNode = doc.CreateCDataSection(cdataContent);
                        currentNode.AppendChild(cdataNode);
                    }
                    else
                    {
                        // Processa conteúdo interno de uma tag como um nó de texto
                        var textNode = doc.CreateTextNode(line);
                        currentNode.AppendChild(textNode);
                    }
                }
            }

            return root;
        }

        private XmlElement ProcessarTagSimples(XmlDocument doc, string line)
        {
            int spaceIndex = line.IndexOf(' ');
            string tagName = line.Substring(1, spaceIndex - 1);
            XmlElement element = doc.CreateElement(tagName);

            AdicionarAtributos(element, line);
            return element;
        }

        private XmlElement ProcessarTagComAtributos(XmlDocument doc, string line)
        {
            // Verificar se a linha contém atributos ou não
            int spaceIndex = line.IndexOf(' ');
            string tagName;

            // Se não encontrar um espaço, extrair apenas o nome da tag
            if (spaceIndex == -1)
            {
                // Considera o caso de uma tag sem atributos, como <Gptbol>
                tagName = line.Substring(1, line.Length - 2); // Remove os < e >
            }
            else
            {
                // Caso normal, onde há atributos
                tagName = line.Substring(1, spaceIndex - 1); // Extrai o nome da tag antes do espaço
            }

            XmlElement element = doc.CreateElement(tagName);

            // Processa atributos se existirem
            if (spaceIndex != -1)
            {
                string attributesPart = line.Substring(spaceIndex, line.Length - spaceIndex - 1); // Remove o >
                string[] attributes = attributesPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var attr in attributes)
                {
                    var keyValue = attr.Split('=');
                    if (keyValue.Length == 2)
                    {
                        string attrName = keyValue[0];
                        string attrValue = keyValue[1].Trim('\'', '"'); // Remove aspas simples ou duplas
                        element.SetAttribute(attrName, attrValue);
                    }
                }
            }

            return element;
        }

        //private XmlElement ProcessarTagComAtributos(XmlDocument doc, string line)
        //{
        //    int spaceIndex = line.IndexOf(' ');
        //    string tagName = line.Substring(1, spaceIndex - 1);
        //    XmlElement element = doc.CreateElement(tagName);

        //    AdicionarAtributos(element, line);
        //    return element;
        //}

        private void AdicionarAtributos(XmlElement element, string line)
        {
            int startIndex = line.IndexOf(' ') + 1;
            int endIndex = line.LastIndexOf('>');
            string attributes = line.Substring(startIndex, endIndex - startIndex).Trim('/');

            var attributePairs = attributes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in attributePairs)
            {
                var kvp = pair.Split('=');
                if (kvp.Length == 2)
                {
                    string name = kvp[0];
                    string value = kvp[1].Trim('\'', '"');
                    element.SetAttribute(name, value);
                }
            }
        }
    }

}
