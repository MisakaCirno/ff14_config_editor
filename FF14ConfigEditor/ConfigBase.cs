using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF14ConfigEditor
{
    /// <summary>
    /// 为了未来可能支持多个配置文件的解析，预留扩展结构。
    /// </summary>
    /// <param name="filePath">配置文件的路径。</param>
    public abstract class ConfigBase(string filePath)
    {
        private readonly string filePath = filePath;

        public string FilePath
        {
            get { return filePath; }
        }

        public abstract void Load();

        public abstract void Save();
    }
}
