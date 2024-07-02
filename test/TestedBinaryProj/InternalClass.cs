namespace TestedBinaryProj
{
    internal class InternalClass
    {
        private void MyPrivateMethod()
        {
            Console.WriteLine("Private Method Called");
        }

        private void MethodToOverride()
        {
            Console.WriteLine("Method to Override Called");
        }

        internal string MyProp
        {
            get;
            private set;
        }
    }
}
