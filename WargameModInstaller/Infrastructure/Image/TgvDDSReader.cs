﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WargameModInstaller.Common.Utilities;
using WargameModInstaller.Model.Image;
using WargameModInstaller.Utilities.Image.DDS;

namespace WargameModInstaller.Infrastructure.Image
{
    /// <summary>
    /// Represents a reader which can read a TGV imgae from a DDS file.
    /// </summary>
    public class TgvDDSReader : ITgvFileReader
    {
        public TgvDDSReader()
        {
            //ale z drugiej strony taki liczbowy limit nic nie daje, potrzeba limit rozmiarowy,
            // bo w przypadku mid jest tylko 9 mipmap bo rozmiar obrazka jest 1024x1024
        }

        public virtual TgvImage Read(String filePath)
        {
            var file = new TgvImage();

            byte[] rawDDSData = File.ReadAllBytes(filePath);
            using (var ms = new MemoryStream(rawDDSData))
            {
                var buffer = new byte[4];
                ms.Read(buffer, 0, buffer.Length);

                if (BitConverter.ToUInt32(buffer, 0) != DDSFormat.MagicHeader)
                {
                    throw new ArgumentException("Wrong DDS magic");
                }

                buffer = new byte[Marshal.SizeOf(typeof(DDSFormat.Header))];
                ms.Read(buffer, 0, buffer.Length);

                var header = MiscUtilities.ByteArrayToStructure<DDSFormat.Header>(buffer);

                if (header.MipMapCount == 0)
                {
                    header.MipMapCount = 1;
                }

                //Może zrobić discard  najmnijszych MipMap, jeśli przekroczono limit (np domyślny 10)?
                uint mipSize = header.Width * header.Height;
                for (ushort i = 0; i < header.MipMapCount; i++)
                {
                    buffer = new byte[mipSize];
                    ms.Read(buffer, 0, buffer.Length);

                    var mip = new TgvMipMap { Content = buffer };
                    file.MipMaps.Add(mip);

                    mipSize /= 4;
                    mipSize = Math.Max(16, mipSize); //16 dla dxt2-5 dla dxt1 powino być 8
                }

                file.Height = header.Height;
                file.ImageHeight = header.Height;
                file.Width = header.Width;
                file.ImageHeight = header.Width;
                file.MipMapCount = (ushort)header.MipMapCount;

                DDSHelper.ConversionFlags conversionFlags;
                file.Format = DDSHelper.GetDXGIFormat(ref header.PixelFormat, out conversionFlags);
            }

            return file;
        }

    }
}
