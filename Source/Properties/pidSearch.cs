﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using static upatcher;
using System.Windows.Forms;

namespace UniversalPatcher.Properties
{
    public class PidSearch
    {
        public PidSearch(PcmFile PCM1)
        {
            PCM = PCM1;
            uint step=8;
            loadPidList();
            startAddress = searchBytes("00 01 02 00 * * * * 00 03 01 00 * * * * 00 04 00 00 * * * * 00 05 00 00", 0, PCM.fsize-28);
            if (startAddress == uint.MaxValue)
            {
                startAddress = searchBytes("00 00 02 00 * * * * * * 00 01 02 00", 0, PCM.fsize - 14);
                step=10;
            }
            if (startAddress == uint.MaxValue)
            {
                startAddress = searchBytes("00 00 04 * * * * * * * 00 01 04 * * * * * * * 00 02 02", 0, PCM.fsize - 23);
                step = 10;
            }
            if (startAddress < uint.MaxValue)
                searchPids(step);
        }

        public class pidName
        {
            public uint PidNumber { get; set; }
            public string PidName { get; set; }
            public string ConversionFactor { get; set; }
        }
        public class PID
        {
            public string PidNumber { get; set; }
            public ushort Bytes { get; set; }
            public string  Subroutine { get; set; }
            public string  RamAddress { get; set; }
            public ushort pidNumberInt;
            public uint SubroutineInt;
            public ushort RamAddressInt;
            public string Name { get; set; }
            public string ConversionFactor { get; set; }
        }
        public uint startAddress { get; set; }
        private PcmFile PCM;
        public List<PID> pidList;
        public List<pidName> pidNameList;

        private void searchPids(uint step)
        {
            pidList = new List<PID>();
            ushort prevPidNumber = 0;            
            for (uint addr = startAddress ; addr < PCM.fsize; addr += step)
            {
                PID pid = readPID(addr);
                if (pid.pidNumberInt < prevPidNumber || pid.pidNumberInt == 0xFFFF)
                {
                    break;
                }
                else
                {
                    pidList.Add(pid);
                }
                prevPidNumber = pid.pidNumberInt;
            }

        }

        private PID readPID(uint addr)
        {
            PID pid = new PID();
            pid.pidNumberInt = BEToUint16(PCM.buf, addr);
            pid.PidNumber = pid.pidNumberInt.ToString("X4");
            pid.Bytes = (ushort)(PCM.buf[addr + 2] + 1);
            if (pid.Bytes == 3)
                pid.Bytes = 4;
            pid.SubroutineInt = BEToUint32(PCM.buf, addr + 4);
            pid.Subroutine = pid.SubroutineInt.ToString("X8");
            uint ramStoreAddr = uint.MaxValue;
            //if (pid.Bytes == 1)
                ramStoreAddr = searchBytes("10 38", pid.SubroutineInt, PCM.fsize, 0x4E75) ;
            //else if (pid.Bytes == 2)
            if (ramStoreAddr == uint.MaxValue)
                ramStoreAddr = searchBytes("30 38", pid.SubroutineInt, PCM.fsize, 0x4E75);
            if (ramStoreAddr < uint.MaxValue)
            {
                pid.RamAddressInt = BEToUint16(PCM.buf, ramStoreAddr + 2);
                pid.RamAddress = pid.RamAddressInt.ToString("X4");
            }
            for (int p=0; p< pidNameList.Count;p++)
            {
                if (pidNameList[p].PidNumber == pid.pidNumberInt)
                {
                    pid.Name = pidNameList[p].PidName;
                    pid.ConversionFactor = pidNameList[p].ConversionFactor;
                    break;
                }
            }
            return pid;
        }
        private uint searchBytes(string searchString, uint Start, uint End, ushort stopVal=0 )
        {
            uint addr;
            string[] searchParts = searchString.Split(' ');

            for (addr=Start; addr < End; addr++)
            {
                bool match = true;
                for (uint part = 0; part < searchParts.Length; part++)
                {
                    if (stopVal != 0 && BEToUint16(PCM.buf,addr) == stopVal)
                    {
                        return uint.MaxValue;
                    }
                    if (searchParts[part] != "*")
                    {
                        byte searchval;
                        HexToByte(searchParts[part], out searchval);
                        if (PCM.buf[addr + part] != searchval)
                        {
                            match = false;
                            break;
                        }
                    }
                }
                if (match)
                {
                    return addr;
                }
            }
            return uint.MaxValue;
        }
        public void loadPidList()
        {
            string FileName = Path.Combine(Application.StartupPath, "XML", "pidlist.xml");
            if (!File.Exists(FileName))
                return;
            pidNameList = new List<pidName>();
            System.Xml.Serialization.XmlSerializer reader = new System.Xml.Serialization.XmlSerializer(typeof(List<pidName>));
            System.IO.StreamReader file = new System.IO.StreamReader(FileName);
            pidNameList = (List<pidName>)reader.Deserialize(file);
            file.Close();
        }

    }
}
