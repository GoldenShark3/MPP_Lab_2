using System;
using FakerLib.PluginSupport;

namespace FakerLib.Generators
{
    public class CharGenerator : Generator<char>
    {
        public override char Generate()
        {
            return (char)new Random().Next(255);
        }
    }
}
