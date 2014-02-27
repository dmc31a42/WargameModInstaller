﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WargameModInstaller.Model.Image;

namespace WargameModInstaller.Infrastructure.Image
{
    /// <summary>
    /// 
    /// </summary>
    public interface ITgvBinWriter
    {
        byte[] Write(TgvImage file);
    }
}
