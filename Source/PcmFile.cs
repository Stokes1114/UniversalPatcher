﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using static upatcher;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Globalization;

namespace UniversalPatcher
{
    public class PcmFile
    {

        public struct V6Table
        {
            public uint address;
            public ushort rows; 
        }
        public class osAddresses
        {
            public osAddresses()
            {

            }
            public string category { get; set; }
            public string label { get; set; }
            public uint address { get; set; }
            public uint size { get; set; }
        }

        public byte[] buf;
        public string FileName;
        public BinFile[] binfile;
        public SegmentInfo[] segmentinfos;
        public uint fsize;
        public string OS;
        public uint osStoreAddress;
        public string mafAddress;
        public bool checksumOK;
        public List<V6Table> v6tables;
        public V6Table v6VeTable;
        public List<osAddresses> osaAddressList;

        public PcmFile(string FName)
        {
            FileName = FName;
            fsize = (uint)new FileInfo(FileName).Length;
            buf = ReadBin(FileName, 0, fsize);
            OS = "";
            osStoreAddress = uint.MaxValue;
        }

        public void loadAddresses()
        {
            string FileName = Path.Combine(Application.StartupPath, "XML", "addresses-" + OS + ".xml");
            if (!File.Exists(FileName))
                return;
            System.Xml.Serialization.XmlSerializer reader = new System.Xml.Serialization.XmlSerializer(typeof(List<osAddresses>));
            System.IO.StreamReader file = new System.IO.StreamReader(FileName);
            osaAddressList = (List<osAddresses>)reader.Deserialize(file);
            file.Close();
        }
        public void GetSegmentAddresses()
        {
            binfile = new BinFile[Segments.Count];
            for (int i = 0; i < Segments.Count; i++)
            {
                SegmentConfig S = Segments[i];
                List<Block> B = new List<Block>();
                binfile[i].ExcludeBlocks = B;
                if (S.Eeprom)
                {
                    //Special case for GM eeprom segment
                    Block eeblock;
                    eeblock.Start = 0x4000;
                    eeblock.End = 0x7fff;
                    B.Add(eeblock);
                    binfile[i].SegmentBlocks = B;
                }
                else if (S.SearchAddresses != null)
                {
                    if (!FindSegment(S, i))
                        return;
                }
                else
                {
                    if (!ParseSegmentAddresses(S.Addresses, S, out B))
                        return;
                    binfile[i].SegmentBlocks = B;
                }
                if (S.SwapAddress != null && S.SwapAddress.Length > 1)
                { 
                    if (!ParseSegmentAddresses(S.SwapAddress, S, out B))
                        return;
                    binfile[i].SwapBlocks = B;
                }
                if (!ParseSegmentAddresses(S.CS1Blocks, S, out B))
                    return;
                binfile[i].CS1Blocks = B;
                if (!ParseSegmentAddresses(S.CS2Blocks, S, out B))
                    return;
                binfile[i].CS2Blocks = B;
                binfile[i].CS1Address = ParseAddress(S.CS1Address, i);
                binfile[i].CS2Address = ParseAddress(S.CS2Address,i);
                if (S.CheckWords != null && S.CheckWords != "")
                    FindCheckwordData(buf, S, ref binfile[i]);

                if (binfile[i].PNaddr.Bytes == 0)  //if not searched
                    binfile[i].PNaddr = ParseAddress(S.PNAddr, i);
                binfile[i].VerAddr = ParseAddress(S.VerAddr,i);
                binfile[i].SegNrAddr = ParseAddress(S.SegNrAddr, i);
                binfile[i].ExtraInfo = ParseExtraInfo(S.ExtraInfo, i);
            }
        }
        public void GetInfo()
        {
            if (SegmentList == null)
                SegmentList = new List<SegmentInfo>();
            segmentinfos = new SegmentInfo[Segments.Count];
            checksumOK = true;
            for (int i = 0; i < Segments.Count; i++)
            {
                SegmentConfig S = Segments[i];
                segmentinfos[i] = new SegmentInfo();
                segmentinfos[i].Name = S.Name;
                segmentinfos[i].FileName = FileName;
                segmentinfos[i].XmlFile = Path.GetFileName(XMLFile);
                string tmp = "";
                uint SSize = 0;
                if (S.SwapAddress != null && S.SwapAddress.Length > 1)
                { 
                    for (int s = 0; s < binfile[i].SwapBlocks.Count; s++)
                    {
                        if (s > 0)
                            tmp += ", ";
                        tmp += binfile[i].SwapBlocks[s].Start.ToString("X4") + " - " + binfile[i].SwapBlocks[s].End.ToString("X4");
                        SSize += binfile[i].SwapBlocks[s].End - binfile[i].SwapBlocks[s].Start + 1;
                    }
                    segmentinfos[i].SwapSize = SSize.ToString("X");
                    segmentinfos[i].SwapAddress = tmp;
                }
                if (S.Eeprom)
                {
                    //Special handling for P01/P59 eeprom -segment
                    GmEeprom.GetEEpromInfo(buf, ref segmentinfos[i]);
                    segmentinfos[i].CS1 = GmEeprom.GetKeyStatus(buf);
                }
                else
                {
                    tmp = "";
                    SSize = 0;
                    for (int s = 0; s < binfile[i].SegmentBlocks.Count; s++)
                    {
                        if (s > 0)
                            tmp += ", ";
                        tmp += binfile[i].SegmentBlocks[s].Start.ToString("X4") + " - " + binfile[i].SegmentBlocks[s].End.ToString("X4");
                        SSize += binfile[i].SegmentBlocks[s].End - binfile[i].SegmentBlocks[s].Start + 1;
                    }
                    segmentinfos[i].Size = SSize.ToString("X");
                    segmentinfos[i].Address = tmp;
                    segmentinfos[i].PN = ReadInfo(binfile[i].PNaddr);
                    if (S.Name == "OS")
                        OS = segmentinfos[i].PN;
                    segmentinfos[i].Ver = ReadInfo(binfile[i].VerAddr);
                    segmentinfos[i].SegNr = ReadInfo(binfile[i].SegNrAddr);
                    if (binfile[i].ExtraInfo != null && binfile[i].ExtraInfo.Count > 0)
                    {
                        string ExtraI = "";
                        for (int e = 0; e < binfile[i].ExtraInfo.Count; e++)
                        {
                            if (e > 0)
                                ExtraI += Environment.NewLine;
                            ExtraI += " " + binfile[i].ExtraInfo[e].Name + ": " + ReadInfo(binfile[i].ExtraInfo[e]);
                        }
                        segmentinfos[i].ExtraInfo = ExtraI;
                    }

                    if (S.CS1Method != CSMethod_None)
                    {
                        string HexLength;
                        if (binfile[i].CS1Address.Bytes == 0)
                        {
                            HexLength = "X4";
                            if (S.CS1Method == CSMethod_crc32 || S.CS1Method == CSMethod_Dwordsum)
                                HexLength = "X8";
                        }
                        else
                        { 
                            HexLength = "X" + (binfile[i].CS1Address.Bytes * 2).ToString();
                        }
                        uint CS1Calc = CalculateChecksum(buf, binfile[i].CS1Address, binfile[i].CS1Blocks, binfile[i].ExcludeBlocks, S.CS1Method, S.CS1Complement, binfile[i].CS1Address.Bytes, S.CS1SwapBytes);
                        segmentinfos[i].CS1Calc = CS1Calc.ToString(HexLength);
                        if (S.CVN == 1)
                        {
                            //segmentinfos[i].cvn = CS1Calc.ToString("X8");
                            segmentinfos[i].cvn = CS1Calc.ToString(HexLength);
                        }
                        if (binfile[i].CS1Address.Address == uint.MaxValue)
                        {
                            segmentinfos[i].Stock = CheckStockCVN(segmentinfos[i].PN, segmentinfos[i].Ver, segmentinfos[i].SegNr, segmentinfos[i].cvn, true).ToString();
                        }
                        else
                        {
                            segmentinfos[i].CS1 = ReadInfo(binfile[i].CS1Address);
                            if (segmentinfos[i].CS1 == segmentinfos[i].CS1Calc)
                            { 
                                if (S.CVN == 1)
                                {
                                    segmentinfos[i].Stock = CheckStockCVN(segmentinfos[i].PN, segmentinfos[i].Ver, segmentinfos[i].SegNr, segmentinfos[i].cvn, true).ToString();
                                }
                            }
                            else
                            {
                                checksumOK = false;
                            }
                        }
                        
                    }

                    if (S.CS2Method != CSMethod_None)
                    {
                        string HexLength;
                        if (binfile[i].CS2Address.Bytes == 0)
                        {
                            HexLength = "X4";
                            if (S.CS2Method == CSMethod_crc32 || S.CS2Method == CSMethod_Dwordsum)
                                HexLength = "X8";
                        }
                        else
                        {
                            HexLength = "X" + (binfile[i].CS2Address.Bytes * 2).ToString();
                        }
                        uint CS2Calc = CalculateChecksum(buf, binfile[i].CS2Address, binfile[i].CS2Blocks, binfile[i].ExcludeBlocks, S.CS2Method, S.CS2Complement, binfile[i].CS2Address.Bytes, S.CS2SwapBytes);
                        segmentinfos[i].CS2Calc = CS2Calc.ToString(HexLength);
                        if (S.CVN == 2)
                        {
                            //segmentinfos[i].cvn = CS2Calc.ToString("X8");
                            segmentinfos[i].cvn = CS2Calc.ToString(HexLength);
                        }

                        if (binfile[i].CS2Address.Address == uint.MaxValue)
                        {
                            segmentinfos[i].Stock = CheckStockCVN(segmentinfos[i].PN, segmentinfos[i].Ver, segmentinfos[i].SegNr, segmentinfos[i].cvn, true).ToString();
                        }
                        else
                        {
                            segmentinfos[i].CS2 = ReadInfo(binfile[i].CS2Address);
                            if (segmentinfos[i].CS2 == segmentinfos[i].CS2Calc)
                            {
                                if (S.CVN == 2)
                                {
                                    segmentinfos[i].Stock = CheckStockCVN(segmentinfos[i].PN, segmentinfos[i].Ver, segmentinfos[i].SegNr, segmentinfos[i].cvn, true).ToString();
                                }
                            }
                            else
                            {
                                checksumOK = false;
                            }
                        }
                    }
                }
                SegmentList.Add(segmentinfos[i]);
            }
            if (!checksumOK)
            {
                for (int i = 0; i< Segments.Count; i++)
                {
                    BadChkFileList.Add(segmentinfos[i]);
                }
            }
            osaAddressList = new List<osAddresses>();
            loadAddresses();
        }

        public bool FindSegment(SegmentConfig S, int SegNr)
        {
            if (!S.Searchfor.Contains(":"))
                throw new Exception("Segment search need 3 parameters: Serch for: search from: Y/N (" + S.Searchfor + ")");
            Debug.WriteLine("Searching segment");

            string[] Parts = S.Searchfor.Split(',');
            foreach (string Part in Parts)
            {
                string[] ForFrom = Part.Split(':');
                if (ForFrom.Length != 3)
                    throw new Exception("Segment search need 3 parameters: Serch for: search from: Y/N (" + Part + ")");
                Debug.WriteLine("Searching for: " + ForFrom[0] + " From: " + ForFrom[1] + " " + ForFrom[2]);

                if (ForFrom[0].Length == 0)
                    return false;
                ushort Bytes = (ushort)(ForFrom[0].Length / 2);
                if (Bytes == 1)
                    Bytes = 2;
                if (Bytes == 3)
                    Bytes = 4;
                if (Bytes > 4 && Bytes < 8)
                    Bytes = 8;
                UInt64 SearchFor;
                if (!HexToUint64(ForFrom[0], out SearchFor))
                    return false;
                uint SearchFrom;
                if (!HexToUint(ForFrom[1], out SearchFrom))
                    return false;
                string SearchNot = ForFrom[2].ToLower();

                List<Block> Blocks;
                binfile[SegNr].SegmentBlocks = new List<Block>();
                if (!ParseSegmentAddresses(S.SearchAddresses, S, out Blocks))
                    return false;
                foreach (Block B in Blocks)
                {

                    uint Addr = B.Start + SearchFrom;
                    if (!SearchNot.StartsWith("n"))
                    {

                        if (Bytes == 8)
                            if (BEToUint64(buf, Addr) == SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 4)
                            if (BEToUint32(buf, Addr) == SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 2)
                            if (BEToUint16(buf, Addr) == SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 1)
                            if (buf[Addr] == SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                    }
                    else
                    {
                        if (Bytes == 8)
                            if (BEToUint64(buf, Addr) != SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 4)
                            if (BEToUint32(buf, Addr) != SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 2)
                            if (BEToUint16(buf, Addr) != SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                        if (Bytes == 1)
                            if (buf[Addr] != SearchFor)
                            {
                                binfile[SegNr].SegmentBlocks.Add(B);
                                Debug.WriteLine("Found: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
                                return true;
                            }
                    }
                }
            }
            Debug.WriteLine("Not found");
            return false;
        }

        public bool FindCheckwordData(byte[] buf, SegmentConfig S, ref BinFile binfile)
        {
            Debug.WriteLine("Checkwords: " + S.CheckWords);
            if (S.CheckWords == null)
                return false;
            binfile.Checkwords = new List<CheckWord>();
            string[] CWlist = S.CheckWords.Split(',');
            foreach (string Row in CWlist)
            {
                string[] Parts = Row.Split(':');
                if (Parts.Length == 4)
                {
                    Debug.WriteLine(Parts[3] + ": " + Parts[0] + " in " + Parts[1] + " ?");
                    CheckWord checkw = new CheckWord();
                    UInt64 CW;
                    uint Location;
                    if (!HexToUint64(Parts[0], out CW))
                        return false;
                    if (!HexToUint(Parts[1], out Location))
                        return false;
                    if (!HexToUint(Parts[2], out checkw.Address))
                        return false;
                    checkw.Key = Parts[3];
                    checkw.Address += binfile.SegmentBlocks[0].Start;

                    Location += binfile.SegmentBlocks[0].Start;
                    ushort Bytes = (ushort)(Parts[0].Length / 2);
                    if (Bytes == 1)
                        Bytes = 2;
                    if (Bytes == 3)
                        Bytes = 4;
                    if (Bytes > 4 && Bytes < 8)
                        Bytes = 8;

                    if (Bytes == 1)
                        if (buf[Location] == CW)
                        {
                            Debug.WriteLine("Checkword: " + checkw.Key + " Found in: " + Location.ToString("X") + ", Data location: " + checkw.Address.ToString("X"));
                            binfile.Checkwords.Add(checkw);
                        }
                    if (Bytes == 2)
                        if (BEToUint16(buf, Location) == CW)
                        {
                            Debug.WriteLine("Checkword: " + checkw.Key + " Found in: " + Location.ToString("X") + ", Data location: " + checkw.Address.ToString("X2"));
                            binfile.Checkwords.Add(checkw);
                        }
                    if (Bytes == 4)
                        if (BEToUint32(buf, Location) == CW)
                        {
                            Debug.WriteLine("Checkword: " + checkw.Key + " Found in: " + Location.ToString("X") + ", Data location: " + checkw.Address.ToString("X4"));
                            binfile.Checkwords.Add(checkw);
                        }
                    if (Bytes == 8)
                        if (BEToUint64(buf, Location) == CW)
                        {
                            Debug.WriteLine("Checkword: " + checkw.Key + " Found in: " + Location.ToString("X") + ", Data location: " + checkw.Address.ToString("X8"));
                            binfile.Checkwords.Add(checkw);
                        }
                }
            }
            return true;
        }


        public string ReadInfo(AddressData AD)
        {
            Debug.WriteLine("Reading address: " + AD.Address.ToString("X") + ", bytes: " + AD.Bytes.ToString() + ", Type: " + AD.Type);
            if (AD.Address == uint.MaxValue)
            {
                Debug.WriteLine("Address not defined");
                return "";
            }
            string Result = "";
            if (AD.Type == TypeFilename)
            {
                Result = AD.Address.ToString();
            }
            else if (AD.Bytes == 1)
            {
                if (AD.Type == TypeHex)
                    Result = buf[AD.Address].ToString("X2");
                else if (AD.Type == TypeText)
                    Result = ReadTextBlock(buf, (int)AD.Address, AD.Bytes);
                else
                    Result = buf[AD.Address].ToString();
            }
            else if (AD.Bytes == 2)
            {
                if (AD.Type == TypeHex)
                    Result = BEToUint16(buf, AD.Address).ToString("X4");
                else if (AD.Type == TypeText)
                    Result = ReadTextBlock(buf, (int)AD.Address, AD.Bytes);
                else
                    Result = BEToUint16(buf, AD.Address).ToString();
            }
            else if (AD.Bytes == 8)
            {
                if (AD.Type == TypeHex)
                    Result = BEToUint64(buf, AD.Address).ToString("X4");
                else if (AD.Type == TypeText)
                    Result = ReadTextBlock(buf, (int)AD.Address, AD.Bytes);
                else
                    Result = BEToUint64(buf, AD.Address).ToString();
            }
            else //Default is 4 bytes
            {
                if (AD.Type == TypeHex)
                    Result = BEToUint32(buf, AD.Address).ToString("X4");
                else if (AD.Type == TypeText)
                    Result = ReadTextBlock(buf, (int)AD.Address, AD.Bytes);
                else
                    Result = BEToUint32(buf, AD.Address).ToString();
            }
            Debug.WriteLine("Result: " + Result);
            return Result;
        }

        private uint FindV6OSAddress(byte[] searchfor)
        {
            uint osStoreAddr = uint.MaxValue;
            for (uint i=0; i < fsize - 6; i++)
            {
                bool match = true;
                for (uint j=0; j<6; j++)
                {
                    if (buf[i + j] != searchfor[j])
                    { 
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    Debug.WriteLine("Found OS Store address from: " + i.ToString("X"));
                    osStoreAddr = i;
                    //Check if this ONLY match
                    for (uint k=i+6; k < fsize - 6; k++)
                    {
                        bool secondmatch = true;
                        for (uint j = 0; j < 6; j++)
                        {
                            if (buf[k + j] != searchfor[j])
                            { 
                                secondmatch = false;
                                break;
                            }
                        }
                        if (secondmatch)
                        {
                            //Found other match, dont know which is correct
                            Debug.WriteLine("Found other match from: " + k.ToString("X") + ", dont know which is correct");
                            return uint.MaxValue;
                        }
                    }
                }
            }
            return osStoreAddr;
        }
        private uint FindV6checksumAddress()
        {
            byte[] searchfor = new byte[6];
            osStoreAddress = uint.MaxValue;

            if (fsize == 256 * 1024)
            {
                searchfor = new byte[] {0x20,0x39,0x0,0x03,0xff,0xfa};
                osStoreAddress = FindV6OSAddress(searchfor);
            }
            if (fsize == 512 * 1024)
            {
                searchfor = new byte[] { 0x26, 0x39, 0x0, 0x07, 0xff, 0xfa };
                osStoreAddress = FindV6OSAddress(searchfor);
                if (osStoreAddress == uint.MaxValue)
                {
                    searchfor = new byte[] { 0x20, 0x39, 0x0, 0x07, 0xff, 0xfa };
                    osStoreAddress = FindV6OSAddress(searchfor);
                }
                if (osStoreAddress == uint.MaxValue)
                {
                    searchfor = new byte[] { 0x20, 0x39, 0x0, 0x07, 0xff, 0xf8 };
                    osStoreAddress = FindV6OSAddress(searchfor);
                }
                if (osStoreAddress == uint.MaxValue)
                {
                    searchfor = new byte[] { 0x26, 0x39, 0x0, 0x07, 0xff, 0xf8 };
                    osStoreAddress = FindV6OSAddress(searchfor);
                }
            }
            if (fsize == 1024 * 1024)
            {
                searchfor = new byte[] { 0x26, 0x39, 0x0, 0x0f, 0xff, 0xfa };
                osStoreAddress = FindV6OSAddress(searchfor);
            }

            if (osStoreAddress == uint.MaxValue)
                return osStoreAddress;

            for (uint i=osStoreAddress + 16; i< osStoreAddress + 32 && i< fsize; i++)
            {
                if (buf[i] == searchfor[0] && buf[i+1] == searchfor[1])
                {
                    return BEToUint32(buf, i + 2);
                }
            }
            //Not found?
            return uint.MaxValue;
        }

        private string FindMafCodes(byte[] searchfor)
        {
            string res = "";
            uint prevMafAddr = uint.MaxValue;
            for (uint i = 0; i < fsize - 6; i++)
            {
                bool match = true;
                for (uint j = 0; j < 6; j++)
                {
                    if (buf[i + j] != searchfor[j])
                    { 
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    Debug.WriteLine("Found MAF address from: " + i.ToString("X"));
                    uint mafAddr = BEToUint32(buf, i + 6);
                    if (mafAddr != prevMafAddr)
                    { 
                        if (res.Length > 0)
                            res += ",";
                        res += mafAddr.ToString("X");
                        prevMafAddr = mafAddr;
                    }
                }
            }
            return res;
        }

        private void FindV6MAFAddress()
        {
            byte[] searchfor = new byte[6];
            mafAddress = "";

            searchfor = new byte[] { 0x30, 0x3C, 0x50, 0x0, 0x20, 0x7c };
            mafAddress = FindMafCodes(searchfor);
            if (mafAddress.Length == 0)
            {
                searchfor = new byte[] { 0x36, 0x3C, 0x50, 0x0, 0x24, 0x7c };
                mafAddress = FindMafCodes(searchfor);
            }

        }
        private V6Table FindVEAddr(byte[] searchfor, ushort length)
        {
            V6Table v6;
            v6.address = uint.MaxValue;
            v6.rows = ushort.MaxValue;
            
            for (uint i = 0; i < fsize - length; i++)
            {
                bool match = true;
                for (uint j = 0; j < length; j++)
                {
                    if (buf[i + j] != searchfor[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    Debug.WriteLine("Found VE search sequence from: " + i.ToString("X"));
                    if (buf[i + length] == 0x74 && buf[i+length+2] == 0x20 &&  buf[i + length + 3] == 0x7C)
                    {
                        Debug.WriteLine("Found V6 VE table from: " + v6.address.ToString("X"));
                        v6.address = BEToUint32(buf, i + length + 4);
                        v6.rows = buf[i + length + 1];
                    }
                }
            }
            return v6;
        }

        private void FindV6VeTable()
        {
            byte[] searchfor = new byte[] { 0x0C, 0x40, 0x1C, 0x00, 0x64, 0x04, 0xE2, 0x48, 0x60, 0x04, 0x30, 0x3C, 0x0E, 0x00 };
            v6VeTable = FindVEAddr(searchfor, 14);
            if (v6VeTable.address == uint.MaxValue)
            {
                searchfor = new byte[] { 0x0C, 0x40, 0x1C, 0x00, 0x64, 0x08, 0xE2, 0x48, 0x04, 0x40, 0x02, 0x00, 0x60, 0x04, 0X30, 0X3C, 0X0C, 0X00 };
                v6VeTable = FindVEAddr(searchfor, 18);
            }
            if (v6VeTable.address == uint.MaxValue)
            {
                searchfor = new byte[] { 0x0C, 0x46, 0x1C, 0x00, 0x64, 0x0A, 0x20, 0x06, 0xE2, 0x48, 0x04, 0x40, 0x02, 0x00, 0X60, 0X04, 0X30, 0X3C, 0x0C, 0x00 };
                v6VeTable = FindVEAddr(searchfor, 20);
            }
        }

        private void FindV6OtherTables()
        {
            v6tables = new List<V6Table>();
            uint calStartAddr = 0;
            if (fsize == 256 * 1024)
                calStartAddr = 0x36000;
            if (fsize == 512 * 1024)
                calStartAddr = 0x64000;
            if (fsize == 1024 * 1024)
                calStartAddr = 0xCF000;

            for (uint i=0;i< fsize-10; i++)
            {
                if (buf[i] == 0x20 && buf[i + 1] == 0x7C && buf[i + 6] == 0x4E && buf[i + 7] == 0xB9)
                {
                    for (uint j=0; j < 18 && i - j > 0; j++)
                    {
                        if (buf[i-j] == 0x74)
                        {
                            uint addr = BEToUint32(buf,i + 2);
                            if (addr < fsize && addr > calStartAddr)
                            { 
                                Debug.WriteLine("Found V6 table address from address: " + (i + 3).ToString("X"));
                                V6Table v6 = new V6Table();
                                v6.address = addr;
                                v6.rows = buf[i - j + 1];
                                v6tables.Add(v6);
                            }
                        }
                    }
                }
            }
        }
        private AddressData GMV6(string Line, int SegNr)
        {
            uint BufSize = (uint)buf.Length;
            uint GMOS = 0;
            binfile[SegNr].PNaddr.Address = 0;
            AddressData AD = new AddressData();

            for (int i = 2; i < 20; i++)
            {
                if (BEToUint16(buf, (uint)(BufSize - i)) == 0xA55A) //Read OS version from end of file, before bytes A5 5A
                {
                    binfile[SegNr].PNaddr.Address = (uint)(BufSize - (i + 4));
                    Debug.WriteLine("V6: Found PN address from: " + binfile[SegNr].PNaddr.Address.ToString("X"));
                }
            }
            if (binfile[SegNr].PNaddr.Address == 0)
                throw new Exception("OS id missing");
            GMOS = BEToUint32(buf, binfile[SegNr].PNaddr.Address);
            binfile[SegNr].PNaddr.Bytes = 4;
            binfile[SegNr].PNaddr.Type = TypeInt;
            Block B = new Block();
            B.Start = binfile[SegNr].PNaddr.Address;
            B.End = binfile[SegNr].PNaddr.Address + 3;
            if (binfile[SegNr].ExcludeBlocks == null)
                binfile[SegNr].ExcludeBlocks = new List<Block>();
            binfile[SegNr].ExcludeBlocks.Add(B);

            FindV6MAFAddress();
            FindV6VeTable();
            FindV6OtherTables();

            AD.Address = FindV6checksumAddress();
            if (AD.Address < uint.MaxValue)
            {
                Debug.WriteLine("Find V6 checksum address: " + AD.Address.ToString("X"));
                AD.Bytes = 4;
                AD.Type = TypeHex;
                return AD;
            }

            Debug.WriteLine("Checksum address not found, using old file-method");

            string FileName = Path.Combine(Application.StartupPath, "XML", Line);
            StreamReader sr = new StreamReader(FileName);
            string OsLine;
            string LastLine = "";
            bool isinfile = false;
            while ((OsLine = sr.ReadLine()) != null)
            {
                //Custom handling: read OS:Segmentaddress pairs from file
                string[] OsLineparts = OsLine.Split(':');
                if (OsLineparts.Length == 2)
                {
                    if (OsLineparts[0] == GMOS.ToString())
                    {
                        isinfile = true;
                        if (HexToUint(OsLineparts[1], out AD.Address))
                        {
                            Debug.WriteLine("Address: " + AD.Address.ToString("X") + ", Bytes: 4, Type: HEX");
                            sr.Close();
                            AD.Bytes = 4;
                            AD.Type = TypeHex;
                            return AD;
                        }
                    }
                    LastLine = OsLine;
                }
            }
            sr.Close();

            if (!isinfile)
            {
                //OS not in file, add it: (OS may be in file, but without address)
                string NewOS = "";
                if (!LastLine.Contains(Environment.NewLine))
                    NewOS = Environment.NewLine;
                NewOS += GMOS.ToString() + ":";
                StreamWriter sw = new StreamWriter(FileName,true);
                sw.WriteLine(NewOS);
                sw.Close();
                throw new Exception("Unsupported OS:  " + GMOS.ToString() + ", adding to file: " + FileName);

            }
            throw new Exception("Unsupported OS:  " + GMOS.ToString());

        }

        public AddressData ParseAddress(string Line, int SegNr)
        {

            Debug.WriteLine("Addressline: " + Line);
            AddressData AD = new AddressData();
            //Set defaults:
            AD.Address = uint.MaxValue;
            AD.Bytes = 2;
            AD.Type = TypeInt;

            if (Line.Length == 0)
            {
                return AD;
            }

            if (Line.StartsWith("GM-V6"))
            {
                //Custom handling: read OS:Segmentaddress pairs from file
                Debug.WriteLine("V6");
                return GMV6(Line, SegNr);
            }


            //Special handling, get info from filename:
            if (Line.StartsWith("filename"))
            {
                if (!Line.Contains(":"))
                    throw new Exception("usage: filename:digits, or filename:digitsmax-digitsmin");
                string[] parts = Line.Split(':');
                ushort digitsmax;
                ushort digitsmin;
                if (parts[1].Contains("-"))
                {
                    string[] digitparts = parts[1].Split('-');
                    if (!ushort.TryParse(digitparts[0], out digitsmax))
                        throw new Exception("usage: filename:digits or filename:digitsmax-digitsmin");
                    if (!ushort.TryParse(digitparts[1], out digitsmin))
                        throw new Exception("usage: filename:digits or filename:digitsmax-digitsmin");
                }
                else 
                {
                    ushort x;
                    if (!ushort.TryParse(parts[1], out x))
                        throw new Exception("usage: filename:digits or filename:digitsmax-digitsmin");
                    digitsmin = x;
                    digitsmax = x;
                }
                for (ushort digits = digitsmax; digits >= digitsmin; digits --)
                { 
                    string[] numbers = Regex.Split(FileName, @"\D+");
                    for  (int v=numbers.Length-1; v >= 0; v-- )
                    {
                        string value = numbers[v];
                        if (!string.IsNullOrEmpty(value) && value.Length == digits)
                        {
                            AD.Address = uint.Parse(value);
                            Debug.WriteLine("PN from filename: {0}", AD.Address);
                            AD.Bytes = digits;
                            AD.Type = TypeFilename;
                            return AD;
                        }
                    }
                }
                //Not found?
                return AD;
            }

            string[] Lineparts = Line.Split(':');
            CheckWord CWAddr;
            CWAddr.Address = 0;
            CWAddr.Key = "";
            bool Negative = false;
            if (Lineparts[0].Contains("CW"))
            {
                CWAddr = GetCheckwordAddress(Lineparts[0], SegNr);
                if (CWAddr.Key != "")
                    Lineparts[0] = Lineparts[0].Replace(CWAddr.Key, "");
            }

            if (Lineparts[0].Replace("#", "") == "")
            {
                //If address is not defined: (For checksum, display-only)
                AD.Address = uint.MaxValue;
            }
            else
            {
                bool relativetoend = false;
                if (Lineparts[0].EndsWith("@"))
                {
                    Lineparts[0] = Lineparts[0].Replace("@", "");
                    relativetoend = true;
                }

                if (!HexToUint(Lineparts[0].Replace("#", ""), out AD.Address))
                    throw new Exception("Can't convert from HEX: " + Lineparts[0].Replace("#", "") + " (" + Line + ")");

                if (relativetoend)
                {
                    if (Line.StartsWith("#"))
                    {
                        AD.Address = binfile[SegNr].SegmentBlocks[(binfile[SegNr].SegmentBlocks.Count - 1)].End - AD.Address;
                    }
                    else
                    {
                        AD.Address = fsize - AD.Address;
                    }
                }
                else
                {
                    if (Line.StartsWith("#"))
                    {
                        AD.Address += binfile[SegNr].SegmentBlocks[0].Start;
                    }
                    if (Negative)
                        AD.Address = CWAddr.Address - AD.Address;
                    else
                        AD.Address += CWAddr.Address;
                }
            }
            //Address handled, handle bytes & type:
            if (Lineparts.Length > 1)
                UInt16.TryParse(Lineparts[1], out AD.Bytes);
            if (Lineparts.Length > 2)
            {
                if (Lineparts[2].ToLower() == "hex")
                    AD.Type = TypeHex;
                else if (Lineparts[2].ToLower() == "text")
                    AD.Type = TypeText;
            }
            Debug.WriteLine("Name: " + AD.Name + ", Address: " + AD.Address.ToString("X") + ", Bytes: " + AD.Bytes.ToString() + ", Type: " + AD.Type.ToString());
            return AD;
        }


        public CheckWord GetCheckwordAddress(string AddrLine, int SegNr)
        {
            string[] LineParts;
            if (AddrLine.Contains("+"))
                LineParts = AddrLine.Split('+');
            else if (AddrLine.Contains("-"))
                LineParts = AddrLine.Split('-');
            else
            {
                throw new Exception("No + or - after Checkword! (" + AddrLine + ")");
            }
            foreach (CheckWord CW in binfile[SegNr].Checkwords)
            {
                if (LineParts[0] == (CW.Key))
                {
                    Debug.WriteLine("Checkword: " + CW.Key + " => " + CW.Address.ToString("X"));
                    return CW;
                }
            }
            CheckWord checkw = new CheckWord();
            checkw.Address = 0;
            checkw.Key = "";
            return checkw;
        }

        public bool ParseSegmentAddresses(string Line, SegmentConfig S, out List<Block> Blocks)
        {
            Debug.WriteLine("Segment address line: " + Line);
            Blocks = new List<Block>();

            if (Line == null || Line == "")
            {
                //It is ok to have empty address (for CS, not for segment)
                Block B = new Block();
                B.End = 0;
                B.Start = 0;
                Blocks.Add(B);
                return true;
            }
            string[] Parts = Line.Split(',');
            int i = 0;

            foreach (string Part in Parts)
            {
                string[] StartEnd = Part.Split('-');
                Block B = new Block();
                int Offset = 0;
                bool isWord = false;
                ushort Multiple = 1;

                if (StartEnd[0].Contains(">"))
                {
                    string[] SO = StartEnd[0].Split('>');
                    StartEnd[0] = SO[0];
                    uint x;
                    if (!HexToUint(SO[1], out x))
                        throw new Exception("Can't decode from HEX: " + SO[1] + " (" + Line + ")");
                    Offset = (int)x;
                }
                if (StartEnd[0].Contains("<"))
                {
                    string[] SO = StartEnd[0].Split('<');
                    StartEnd[0] = SO[0];
                    uint x;
                    if (!HexToUint(SO[1], out x))
                        throw new Exception("Can't decode from HEX: " + SO[1] + " (" + Line + ")");
                    Offset = ~(int)x;
                }


                if (StartEnd[0].Contains("*"))
                {
                    string[] SM = StartEnd[0].Split('*');
                    StartEnd[0] = SM[0];
                    UInt16.TryParse(SM[1], out Multiple);
                }

                if (StartEnd[0].Contains(":2"))
                {
                    string[] SW = StartEnd[0].Split(':');
                    StartEnd[0] = SW[0];
                    isWord = true;
                }

                if (StartEnd.Length > 1 && StartEnd[1].Contains(":2"))
                {
                    string[] EW = StartEnd[1].Split(':');
                    StartEnd[1] = EW[0];
                    isWord = true;
                }


                if (!HexToUint(StartEnd[0].Replace("@", ""), out B.Start))
                {
                    throw new Exception("Can't decode from HEX: " + StartEnd[0].Replace("@", "") + " (" + Line + ")");
                }
                if (StartEnd[0].StartsWith("@"))
                {
                    uint tmpStart = B.Start;
                    for (int m = 1; m <= Multiple; m++)
                    {
                        //Read address from bin at this address

                        if (isWord)
                        {
                            B.Start = BEToUint16(buf, tmpStart);
                            B.End = BEToUint16(buf, tmpStart + 2);
                            tmpStart += 4;
                        }
                        else
                        {
                            B.Start = BEToUint32(buf, tmpStart);
                            B.End = BEToUint32(buf, tmpStart + 4);
                            tmpStart += 8;
                        }
                        if (Multiple > 1)
                        {
                            // Have multiple start-end pairs
                            B.Start += (uint)Offset;
                            B.End += (uint)Offset;
                            Blocks.Add(B);
                        }
                    }
                }
                else
                {
                    if (!HexToUint(StartEnd[1].Replace("@", ""), out B.End))
                        throw new Exception("Can't decode from HEX: " + StartEnd[1].Replace("@", "") + " (" + Line + ")");
                    if (B.End >= buf.Length)    //Make 1MB config work with 512kB bin
                        B.End = (uint)buf.Length - 1;
                }
                if (Multiple < 2)
                {
                    if (StartEnd.Length > 1 && StartEnd[1].StartsWith("@"))
                    {
                        //Read End address from bin at this address
                        B.End = BEToUint32(buf, B.End);
                    }
                    if (StartEnd.Length > 1 && StartEnd[1].EndsWith("@"))
                    {
                        //Address is relative to end of bin
                        uint end;
                        if (HexToUint(StartEnd[1].Replace("@", ""), out end))
                            B.End = (uint)buf.Length - end - 1;
                    }
                    B.Start += (uint)Offset;
                    B.End += (uint)Offset;
                    Blocks.Add(B);
                }
                i++;
            }
            foreach (Block B in Blocks)
                Debug.WriteLine("Address block: " + B.Start.ToString("X") + " - " + B.End.ToString("X"));
            return true;
        }

        public List<AddressData> ParseExtraInfo(string Line, int SegNr)
        {
            Debug.WriteLine("Extrainfo: " + Line);
            List<AddressData> LEX = new List<AddressData>();
            if (Line == null || Line.Length == 0 || !Line.Contains(":"))
                return LEX;

            string[] LineParts = Line.Split(',');
            foreach (string LinePart in LineParts)
            {
                AddressData E = new AddressData();
                string[] AddrParts = LinePart.Split(':');
                if (AddrParts.Length < 3)
                    return LEX;

                E.Name = AddrParts[0];

                CheckWord CWAddr;
                CWAddr.Key = "";
                CWAddr.Address = 0;
                bool Negative = false;
                if (AddrParts[1].Contains("CW"))
                {
                    CWAddr = GetCheckwordAddress(AddrParts[1], SegNr);
                    if (CWAddr.Key != "")
                        AddrParts[1] = AddrParts[1].Replace(CWAddr.Key, "");
                }
                if (AddrParts[1].Contains("-"))
                    Negative = true;
                AddrParts[1] = AddrParts[1].Replace("-", "");
                AddrParts[1] = AddrParts[1].Replace("+", "");
                if (!HexToUint(AddrParts[1].Replace("#", ""), out E.Address))
                    return LEX;

                if (Negative)
                    E.Address = CWAddr.Address - E.Address;
                else
                    E.Address += CWAddr.Address;

                if (AddrParts[1].StartsWith("#"))
                {
                    E.Address += binfile[SegNr].SegmentBlocks[0].Start;
                }

                if (AddrParts.Length > 2)
                    UInt16.TryParse(AddrParts[2], out E.Bytes);
                E.Type = TypeInt;
                if (AddrParts.Length > 3)
                {
                    if (AddrParts[3].ToLower() == "hex")
                        E.Type = TypeHex;
                    else if (AddrParts[3].ToLower() == "text")
                        E.Type = TypeText;
                }
                LEX.Add(E);
            }
            for (int l = 0; l < LEX.Count; l++)
                Debug.WriteLine("Extrainfo name: " + LEX[l].Name + ", Address: " + LEX[l].Address.ToString("X") + ", Bytes: " + LEX[l].Bytes + ", Type: " + LEX[l].Type);
            return LEX;
        }

    }
}
