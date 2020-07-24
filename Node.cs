namespace ConnectToOPC
{
    class Node
    {
        public Node()
        {
        }

        public string BrowseName;
        public string NodeId;
        public string Value;

        public void SetName(string name) =>
            BrowseName = name;

        public void SetNodeId(string nodeid) =>
            NodeId = nodeid;

        public void SetValue(string value) =>
            Value = value;
    }
}
