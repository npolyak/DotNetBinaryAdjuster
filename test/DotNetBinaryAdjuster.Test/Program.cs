using System;
using TestedBinaryProj;

namespace DotNetBinaryAdjuster.Test
{
    public class MyInheritedClass : InternalClass
    { 
        protected override void MethodToOverride()
        {
            Console.WriteLine("Successfully Overrode MethodToOverride");
        }

        public void CallMethodToOverride()
        {
            MethodToOverride();
        }
    }


    public class Program
    {
        

        static void Main(string[] args)
        {
            MyInheritedClass myInheritedClass = new MyInheritedClass();

            myInheritedClass.MyProp = "Hello World!";

            Console.WriteLine(myInheritedClass.MyProp);

            myInheritedClass.CallMethodToOverride();

            myInheritedClass.MyPrivateMethod();
        }
    }
}
