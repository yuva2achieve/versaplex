using System;
using System.Collections.Generic;
using NDesk.DBus;
using Wv;

// The checksums for a single database element (table, procedure, etc).
// Can have multiple checksum values - a table has one checksum per column,
// for instance.
// FIXME: Have separate type member?
internal class VxSchemaChecksum
{
    string _name;
    public string name {
        get { return _name; }
    }

    List<ulong> _checksums;
    public List<ulong> checksums {
        get { return _checksums; }
    }

    public VxSchemaChecksum(string newname, ulong newchecksum)
    {
        _name = newname;
        _checksums = new List<ulong>();
        AddChecksum(newchecksum);
    }

    // Read a set of checksums from a DBus message into a VxSchemaChecksum.
    public VxSchemaChecksum(MessageReader reader)
    {
        reader.GetValue(out _name);
        _checksums = new List<ulong>();

        // Fill the list
        int size;
        reader.GetValue(out size);
        int endpos = reader.Position + size;
        while (reader.Position < endpos)
        {
            reader.ReadPad(8);
            ulong sum;
            reader.GetValue(out sum);
            AddChecksum(sum);
        }
    }

    private void _WriteSums(MessageWriter writer)
    {
        foreach (ulong sum in _checksums)
            writer.Write(sum);
    }

    // Write the checksum values to DBus
    public void Write(MessageWriter writer)
    {
        writer.Write(typeof(string), name);
        writer.WriteDelegatePrependSize(delegate(MessageWriter w)
            {
                _WriteSums(w);
            }, 8);
    }

    public void AddChecksum(ulong checksum)
    {
        _checksums.Add(checksum);
    }

    public static string GetSignature()
    {
        return "sat";
    }
}

// The checksum values for a set of database elements
internal class VxSchemaChecksums : Dictionary<string, VxSchemaChecksum>
{
    public VxSchemaChecksums()
    {
    }

    // Read an array of checksums from a DBus message.
    public VxSchemaChecksums(MessageReader reader)
    {
        int size;
        reader.GetValue(out size);
        int endpos = reader.Position + size;
        while (reader.Position < endpos)
        {
            reader.ReadPad(8);
            VxSchemaChecksum cs = new VxSchemaChecksum(reader);
            Add(cs.name, cs);
        }
    }

    private void _WriteChecksums(MessageWriter writer)
    {
        foreach (KeyValuePair<string,VxSchemaChecksum> p in this)
        {
            writer.WritePad(8);
            p.Value.Write(writer);
        }
    }

    // Write the list of checksums to DBus in a(sat) format.
    public void WriteChecksums(MessageWriter writer)
    {
        writer.WriteDelegatePrependSize(delegate(MessageWriter w)
            {
                _WriteChecksums(w);
            }, 8);
    }

    public void Add(string name, ulong checksum)
    {
        if (this.ContainsKey(name))
            this[name].AddChecksum(checksum);
        else
            this.Add(name, new VxSchemaChecksum(name, checksum));
    }

    public static string GetSignature()
    {
        return String.Format("a({0})", VxSchemaChecksum.GetSignature());
    }
}
