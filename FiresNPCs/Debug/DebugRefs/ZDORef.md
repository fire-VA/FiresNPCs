// Decompiled with JetBrains decompiler
// Type: ZDO
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800
// Assembly location: C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#nullable disable
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ZDO : IEquatable<ZDO>
{
  public ZDOID m_uid = ZDOID.None;
  public float m_tempSortValue;
  private int m_prefab;
  private Vector2s m_sector = Vector2s.zero;
  private Vector3 m_rotation = Quaternion.identity.eulerAngles;
  private Vector3 m_position = Vector3.zero;
  private ZDO.DataFlags m_dataFlags;
  private byte m_tempRemoveEarmark = byte.MaxValue;

  public void Initialize(ZDOID id, Vector3 position)
  {
    this.m_uid = id;
    this.m_position = position;
    Vector2i zone = ZoneSystem.GetZone(this.m_position);
    this.m_sector = zone.ClampToShort();
    ZDOMan.instance.AddToSector(this, zone);
    this.m_dataFlags = ZDO.DataFlags.None;
    this.Valid = true;
  }

  public void Init()
  {
    this.m_dataFlags = ZDO.DataFlags.None;
    this.Valid = true;
  }

  public override string ToString() => this.m_uid.ToString();

  public bool IsValid() => this.Valid;

  public override int GetHashCode() => this.m_uid.GetHashCode();

  public bool Equals(ZDO other) => this == other;

  public void Reset()
  {
    if (!this.SaveClone)
      ZDOExtraData.Release(this, this.m_uid);
    this.m_uid = ZDOID.None;
    this.m_dataFlags = ZDO.DataFlags.None;
    this.OwnerRevision = (ushort) 0;
    this.DataRevision = 0U;
    this.m_tempSortValue = 0.0f;
    this.m_prefab = 0;
    this.m_sector = Vector2s.zero;
    this.m_position = Vector3.zero;
    this.m_rotation = Quaternion.identity.eulerAngles;
  }

  public ZDO Clone()
  {
    ZDO zdo = this.MemberwiseClone() as ZDO;
    zdo.SaveClone = true;
    return zdo;
  }

  public void Set(string name, ZDOID id) => this.Set(ZDO.GetHashZDOID(name), id);

  public void Set(KeyValuePair<int, int> hashPair, ZDOID id)
  {
    this.Set(hashPair.Key, id.UserID);
    this.Set(hashPair.Value, (long) id.ID);
  }

  public static KeyValuePair<int, int> GetHashZDOID(string name)
  {
    return new KeyValuePair<int, int>((name + "_u").GetStableHashCode(), (name + "_i").GetStableHashCode());
  }

  public ZDOID GetZDOID(string name) => this.GetZDOID(ZDO.GetHashZDOID(name));

  public ZDOID GetZDOID(KeyValuePair<int, int> hashPair)
  {
    long userID = this.GetLong(hashPair.Key);
    uint id = (uint) this.GetLong(hashPair.Value);
    return userID == 0L || id == 0U ? ZDOID.None : new ZDOID(userID, id);
  }

  public void Set(string name, float value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, float value)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, Vector3 value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, Vector3 value)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void Update(int hash, Vector3 value)
  {
    if (!ZDOExtraData.Update(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, Quaternion value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, Quaternion value)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, int value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, int value, bool okForNotOwner = false)
  {
    int num = okForNotOwner ? 1 : 0;
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void SetConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
  {
    if (!ZDOExtraData.SetConnection(this.m_uid, connectionType, zid))
      return;
    this.IncreaseDataRevision();
  }

  public void UpdateConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
  {
    if (!ZDOExtraData.UpdateConnection(this.m_uid, connectionType, zid))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, bool value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, bool value) => this.Set(hash, value ? 1 : 0);

  public void Set(string name, long value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, long value)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, byte[] bytes) => this.Set(name.GetStableHashCode(), bytes);

  public void Set(int hash, byte[] bytes)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, bytes))
      return;
    this.IncreaseDataRevision();
  }

  public void Set(string name, string value) => this.Set(name.GetStableHashCode(), value);

  public void Set(int hash, string value)
  {
    if (!ZDOExtraData.Set(this.m_uid, hash, value))
      return;
    this.IncreaseDataRevision();
  }

  public void SetPosition(Vector3 pos) => this.InternalSetPosition(pos);

  public void InternalSetPosition(Vector3 pos)
  {
    if (this.m_position == pos)
      return;
    this.m_position = pos;
    this.SetSector(ZoneSystem.GetZone(this.m_position));
    if (!this.IsOwner())
      return;
    this.IncreaseDataRevision();
  }

  public void InvalidateSector() => this.SetSector(new Vector2i(int.MinValue, int.MinValue));

  private void SetSector(Vector2i sector)
  {
    if (this.m_sector == sector)
      return;
    ZDOMan.instance.RemoveFromSector(this, this.m_sector.ToVector2i());
    this.m_sector = sector.ClampToShort();
    ZDOMan.instance.AddToSector(this, sector);
    if (!ZNet.instance.IsServer())
      return;
    ZDOMan.instance.ZDOSectorInvalidated(this);
  }

  public Vector2i GetSector() => this.m_sector.ToVector2i();

  public void SetRotation(Quaternion rot)
  {
    if (this.m_rotation == rot.eulerAngles)
      return;
    this.m_rotation = rot.eulerAngles;
    this.IncreaseDataRevision();
  }

  public void SetType(ZDO.ObjectType type)
  {
    if (this.Type == type)
      return;
    this.Type = type;
    this.IncreaseDataRevision();
  }

  public void SetDistant(bool distant)
  {
    if (this.Distant == distant)
      return;
    this.Distant = distant;
    this.IncreaseDataRevision();
  }

  public void SetPrefab(int prefab)
  {
    if (this.m_prefab == prefab)
      return;
    this.m_prefab = prefab;
    this.IncreaseDataRevision();
  }

  public int GetPrefab() => this.m_prefab;

  public Vector3 GetPosition() => this.m_position;

  public Quaternion GetRotation() => Quaternion.Euler(this.m_rotation);

  private void IncreaseDataRevision()
  {
    ++this.DataRevision;
    if (ZNet.instance.IsServer())
      return;
    ZDOMan.instance.ClientChanged(this.m_uid);
  }

  private void IncreaseOwnerRevision()
  {
    ++this.OwnerRevision;
    if (ZNet.instance.IsServer())
      return;
    ZDOMan.instance.ClientChanged(this.m_uid);
  }

  public float GetFloat(string name, float defaultValue = 0.0f)
  {
    return this.GetFloat(name.GetStableHashCode(), defaultValue);
  }

  public float GetFloat(int hash, float defaultValue = 0.0f)
  {
    return ZDOExtraData.GetFloat(this.m_uid, hash, defaultValue);
  }

  public bool GetFloat(string name, out float value)
  {
    return this.GetFloat(name.GetStableHashCode(), out value);
  }

  public bool GetFloat(int hash, out float value)
  {
    return ZDOExtraData.GetFloat(this.m_uid, hash, out value);
  }

  public Vector3 GetVec3(string name, Vector3 defaultValue)
  {
    return this.GetVec3(name.GetStableHashCode(), defaultValue);
  }

  public Vector3 GetVec3(int hash, Vector3 defaultValue)
  {
    return ZDOExtraData.GetVec3(this.m_uid, hash, defaultValue);
  }

  public bool GetVec3(string name, out Vector3 value)
  {
    return this.GetVec3(name.GetStableHashCode(), out value);
  }

  public bool GetVec3(int hash, out Vector3 value)
  {
    return ZDOExtraData.GetVec3(this.m_uid, hash, out value);
  }

  public Quaternion GetQuaternion(string name, Quaternion defaultValue)
  {
    return this.GetQuaternion(name.GetStableHashCode(), defaultValue);
  }

  public Quaternion GetQuaternion(int hash, Quaternion defaultValue)
  {
    return ZDOExtraData.GetQuaternion(this.m_uid, hash, defaultValue);
  }

  public bool GetQuaternion(string name, out Quaternion value)
  {
    return this.GetQuaternion(name.GetStableHashCode(), out value);
  }

  public bool GetQuaternion(int hash, out Quaternion value)
  {
    return ZDOExtraData.GetQuaternion(this.m_uid, hash, out value);
  }

  public int GetInt(string name, int defaultValue = 0)
  {
    return this.GetInt(name.GetStableHashCode(), defaultValue);
  }

  public int GetInt(int hash, int defaultValue = 0)
  {
    return ZDOExtraData.GetInt(this.m_uid, hash, defaultValue);
  }

  public bool GetInt(string name, out int value)
  {
    return this.GetInt(name.GetStableHashCode(), out value);
  }

  public bool GetInt(int hash, out int value) => ZDOExtraData.GetInt(this.m_uid, hash, out value);

  public bool GetBool(string name, bool defaultValue = false)
  {
    return this.GetBool(name.GetStableHashCode(), defaultValue);
  }

  public bool GetBool(int hash, bool defaultValue = false)
  {
    return ZDOExtraData.GetInt(this.m_uid, hash, defaultValue ? 1 : 0) != 0;
  }

  public bool GetBool(string name, out bool value)
  {
    return this.GetBool(name.GetStableHashCode(), out value);
  }

  public bool GetBool(int hash, out bool value)
  {
    return ZDOExtraData.GetBool(this.m_uid, hash, out value);
  }

  public long GetLong(string name, long defaultValue = 0)
  {
    return this.GetLong(name.GetStableHashCode(), defaultValue);
  }

  public long GetLong(int hash, long defaultValue = 0)
  {
    return ZDOExtraData.GetLong(this.m_uid, hash, defaultValue);
  }

  public string GetString(string name, string defaultValue = "")
  {
    return this.GetString(name.GetStableHashCode(), defaultValue);
  }

  public string GetString(int hash, string defaultValue = "")
  {
    return ZDOExtraData.GetString(this.m_uid, hash, defaultValue);
  }

  public bool GetString(string name, out string value)
  {
    return this.GetString(name.GetStableHashCode(), out value);
  }

  public bool GetString(int hash, out string value)
  {
    return ZDOExtraData.GetString(this.m_uid, hash, out value);
  }

  public byte[] GetByteArray(string name, byte[] defaultValue = null)
  {
    return this.GetByteArray(name.GetStableHashCode(), defaultValue);
  }

  public byte[] GetByteArray(int hash, byte[] defaultValue = null)
  {
    return ZDOExtraData.GetByteArray(this.m_uid, hash, defaultValue);
  }

  public bool GetByteArray(string name, out byte[] value)
  {
    return this.GetByteArray(name.GetStableHashCode(), out value);
  }

  public bool GetByteArray(int hash, out byte[] value)
  {
    return ZDOExtraData.GetByteArray(this.m_uid, hash, out value);
  }

  public ZDOID GetConnectionZDOID(ZDOExtraData.ConnectionType type)
  {
    return ZDOExtraData.GetConnectionZDOID(this.m_uid, type);
  }

  public ZDOExtraData.ConnectionType GetConnectionType()
  {
    return ZDOExtraData.GetConnectionType(this.m_uid);
  }

  public ZDOConnection GetConnection() => ZDOExtraData.GetConnection(this.m_uid);

  public ZDOConnectionHashData GetConnectionHashData(ZDOExtraData.ConnectionType type)
  {
    return ZDOExtraData.GetConnectionHashData(this.m_uid, type);
  }

  public bool RemoveInt(string name) => this.RemoveInt(name.GetStableHashCode());

  public bool RemoveInt(int hash) => ZDOExtraData.RemoveInt(this.m_uid, hash);

  public bool RemoveLong(int hash) => ZDOExtraData.RemoveLong(this.m_uid, hash);

  public bool RemoveFloat(string name) => this.RemoveFloat(name.GetStableHashCode());

  public bool RemoveFloat(int hash) => ZDOExtraData.RemoveFloat(this.m_uid, hash);

  public bool RemoveVec3(string name) => this.RemoveVec3(name.GetStableHashCode());

  public bool RemoveVec3(int hash) => ZDOExtraData.RemoveVec3(this.m_uid, hash);

  public bool RemoveQuaternion(string name) => this.RemoveQuaternion(name.GetStableHashCode());

  public bool RemoveQuaternion(int hash) => ZDOExtraData.RemoveQuaternion(this.m_uid, hash);

  public void RemoveZDOID(string name)
  {
    KeyValuePair<int, int> hashZdoid = ZDO.GetHashZDOID(name);
    ZDOExtraData.RemoveLong(this.m_uid, hashZdoid.Key);
    ZDOExtraData.RemoveLong(this.m_uid, hashZdoid.Value);
  }

  public void RemoveZDOID(KeyValuePair<int, int> hashes)
  {
    ZDOExtraData.RemoveLong(this.m_uid, hashes.Key);
    ZDOExtraData.RemoveLong(this.m_uid, hashes.Value);
  }

  public void Serialize(ZPackage pkg)
  {
    List<KeyValuePair<int, float>> floats = ZDOExtraData.GetFloats(this.m_uid);
    List<KeyValuePair<int, Vector3>> vec3s = ZDOExtraData.GetVec3s(this.m_uid);
    List<KeyValuePair<int, Quaternion>> quaternions = ZDOExtraData.GetQuaternions(this.m_uid);
    List<KeyValuePair<int, int>> ints = ZDOExtraData.GetInts(this.m_uid);
    List<KeyValuePair<int, long>> longs = ZDOExtraData.GetLongs(this.m_uid);
    List<KeyValuePair<int, string>> strings = ZDOExtraData.GetStrings(this.m_uid);
    List<KeyValuePair<int, byte[]>> byteArrays = ZDOExtraData.GetByteArrays(this.m_uid);
    ZDOConnection connection = ZDOExtraData.GetConnection(this.m_uid);
    ushort num = 0;
    if (connection != null && connection.m_type != ZDOExtraData.ConnectionType.None)
      num |= (ushort) 1;
    if (floats.Count > 0)
      num |= (ushort) 2;
    if (vec3s.Count > 0)
      num |= (ushort) 4;
    if (quaternions.Count > 0)
      num |= (ushort) 8;
    if (ints.Count > 0)
      num |= (ushort) 16 /*0x10*/;
    if (longs.Count > 0)
      num |= (ushort) 32 /*0x20*/;
    if (strings.Count > 0)
      num |= (ushort) 64 /*0x40*/;
    if (byteArrays.Count > 0)
      num |= (ushort) 128 /*0x80*/;
    bool flag = this.m_rotation != Quaternion.identity.eulerAngles;
    ushort data = (ushort) ((int) (ushort) ((uint) (ushort) ((int) (ushort) ((int) num | (this.Persistent ? 256 /*0x0100*/ : 0)) | (this.Distant ? 512 /*0x0200*/ : 0)) | (uint) (ushort) ((uint) this.Type << 10)) | (flag ? 4096 /*0x1000*/ : 0));
    pkg.Write(data);
    pkg.Write(this.m_prefab);
    if (flag)
      pkg.Write(this.m_rotation);
    if (((int) data & (int) byte.MaxValue) == 0)
      return;
    if (((int) data & 1) != 0)
    {
      pkg.Write((byte) connection.m_type);
      pkg.Write(connection.m_target);
    }
    if (floats.Count > 0)
    {
      pkg.Write((byte) floats.Count);
      foreach (KeyValuePair<int, float> keyValuePair in floats)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (vec3s.Count > 0)
    {
      pkg.Write((byte) vec3s.Count);
      foreach (KeyValuePair<int, Vector3> keyValuePair in vec3s)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (quaternions.Count > 0)
    {
      pkg.Write((byte) quaternions.Count);
      foreach (KeyValuePair<int, Quaternion> keyValuePair in quaternions)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (ints.Count > 0)
    {
      pkg.Write((byte) ints.Count);
      foreach (KeyValuePair<int, int> keyValuePair in ints)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (longs.Count > 0)
    {
      pkg.Write((byte) longs.Count);
      foreach (KeyValuePair<int, long> keyValuePair in longs)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (strings.Count > 0)
    {
      pkg.Write((byte) strings.Count);
      foreach (KeyValuePair<int, string> keyValuePair in strings)
      {
        pkg.Write(keyValuePair.Key);
        pkg.Write(keyValuePair.Value);
      }
    }
    if (byteArrays.Count <= 0)
      return;
    pkg.Write((byte) byteArrays.Count);
    foreach (KeyValuePair<int, byte[]> keyValuePair in byteArrays)
    {
      pkg.Write(keyValuePair.Key);
      pkg.Write(keyValuePair.Value);
    }
  }

  public void Deserialize(ZPackage pkg)
  {
    ushort num1 = pkg.ReadUShort();
    this.Persistent = ((uint) num1 & 256U /*0x0100*/) > 0U;
    this.Distant = ((uint) num1 & 512U /*0x0200*/) > 0U;
    this.Type = (ZDO.ObjectType) ((int) num1 >> 10 & 3);
    this.m_prefab = pkg.ReadInt();
    if (((uint) num1 & 4096U /*0x1000*/) > 0U)
      this.m_rotation = pkg.ReadVector3();
    if (((int) num1 & (int) byte.MaxValue) == 0)
      return;
    int num2 = ((uint) num1 & 1U) > 0U ? 1 : 0;
    bool flag1 = ((uint) num1 & 2U) > 0U;
    bool flag2 = ((uint) num1 & 4U) > 0U;
    bool flag3 = ((uint) num1 & 8U) > 0U;
    bool flag4 = ((uint) num1 & 16U /*0x10*/) > 0U;
    bool flag5 = ((uint) num1 & 32U /*0x20*/) > 0U;
    bool flag6 = ((uint) num1 & 64U /*0x40*/) > 0U;
    bool flag7 = ((uint) num1 & 128U /*0x80*/) > 0U;
    if (num2 != 0)
      ZDOExtraData.SetConnection(this.m_uid, (ZDOExtraData.ConnectionType) pkg.ReadByte(), pkg.ReadZDOID());
    if (flag1)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Float, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadSingle());
    }
    if (flag2)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Vec3, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadVector3());
    }
    if (flag3)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Quat, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadQuaternion());
    }
    if (flag4)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Int, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadInt());
    }
    if (flag5)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Long, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadLong());
    }
    if (flag6)
    {
      int size = (int) pkg.ReadByte();
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.String, size);
      for (int index = 0; index < size; ++index)
        ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadString());
    }
    if (!flag7)
      return;
    int size1 = (int) pkg.ReadByte();
    ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.ByteArray, size1);
    for (int index = 0; index < size1; ++index)
      ZDOExtraData.Set(this.m_uid, pkg.ReadInt(), pkg.ReadByteArray());
  }

  public void Save(ZPackage pkg)
  {
    List<KeyValuePair<int, float>> saveFloats = ZDOExtraData.GetSaveFloats(this.m_uid);
    List<KeyValuePair<int, Vector3>> saveVec3s = ZDOExtraData.GetSaveVec3s(this.m_uid);
    List<KeyValuePair<int, Quaternion>> saveQuaternions = ZDOExtraData.GetSaveQuaternions(this.m_uid);
    List<KeyValuePair<int, int>> saveInts = ZDOExtraData.GetSaveInts(this.m_uid);
    List<KeyValuePair<int, long>> saveLongs = ZDOExtraData.GetSaveLongs(this.m_uid);
    List<KeyValuePair<int, string>> saveStrings = ZDOExtraData.GetSaveStrings(this.m_uid);
    List<KeyValuePair<int, byte[]>> saveByteArrays = ZDOExtraData.GetSaveByteArrays(this.m_uid);
    ZDOConnectionHashData saveConnections = ZDOExtraData.GetSaveConnections(this.m_uid);
    ushort num = 0;
    if (saveConnections != null && saveConnections.m_type != ZDOExtraData.ConnectionType.None)
      num |= (ushort) 1;
    if (saveFloats.Count > 0)
      num |= (ushort) 2;
    if (saveVec3s.Count > 0)
      num |= (ushort) 4;
    if (saveQuaternions.Count > 0)
      num |= (ushort) 8;
    if (saveInts.Count > 0)
      num |= (ushort) 16 /*0x10*/;
    if (saveLongs.Count > 0)
      num |= (ushort) 32 /*0x20*/;
    if (saveStrings.Count > 0)
      num |= (ushort) 64 /*0x40*/;
    if (saveByteArrays.Count > 0)
      num |= (ushort) 128 /*0x80*/;
    bool flag = this.m_rotation != Quaternion.identity.eulerAngles;
    ushort data = (ushort) ((int) (ushort) ((uint) (ushort) ((int) (ushort) ((int) num | (this.Persistent ? 256 /*0x0100*/ : 0)) | (this.Distant ? 512 /*0x0200*/ : 0)) | (uint) (ushort) ((uint) this.Type << 10)) | (flag ? 4096 /*0x1000*/ : 0));
    pkg.Write(data);
    pkg.Write(this.m_sector);
    pkg.Write(this.m_position);
    pkg.Write(this.m_prefab);
    if (flag)
      pkg.Write(this.m_rotation);
    if (((int) data & (int) byte.MaxValue) == 0)
      return;
    if (((int) data & 1) != 0)
    {
      pkg.Write((byte) saveConnections.m_type);
      pkg.Write(saveConnections.m_hash);
    }
    ZDODataHelper.WriteData<float>(pkg, saveFloats, (Action<float>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<Vector3>(pkg, saveVec3s, (Action<Vector3>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<Quaternion>(pkg, saveQuaternions, (Action<Quaternion>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<int>(pkg, saveInts, (Action<int>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<long>(pkg, saveLongs, (Action<long>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<string>(pkg, saveStrings, (Action<string>) (value => pkg.Write(value)));
    ZDODataHelper.WriteData<byte[]>(pkg, saveByteArrays, (Action<byte[]>) (value => pkg.Write(value)));
  }

  private static bool Strip(int key) => ZDOHelper.s_stripOldData.Contains(key);

  private static bool StripLong(int key) => ZDOHelper.s_stripOldLongData.Contains(key);

  private static bool Strip(int key, long data) => ZDO.StripLong(key) || ZDO.Strip(key);

  private static bool Strip(int key, int data) => ZDO.Strip(key);

  private static bool Strip(int key, Quaternion data)
  {
    return data == Quaternion.identity || ZDO.Strip(key);
  }

  private static bool Strip(int key, string data) => string.IsNullOrEmpty(data) || ZDO.Strip(key);

  private static bool Strip(int key, byte[] data)
  {
    return data.Length == 0 || ZDOHelper.s_stripOldDataByteArray.Contains(key);
  }

  private static bool StripConvert(ZDOID zid, int key, long data)
  {
    if (ZDO.Strip(key))
      return true;
    if (key != ZDOVars.s_SpawnTime__DontUse && key != ZDOVars.s_spawn_time__DontUse)
      return false;
    ZDOExtraData.Set(zid, ZDOVars.s_spawnTime, data);
    return true;
  }

  private static bool StripConvert(ZDOID zid, int key, Vector3 data)
  {
    if (ZDO.Strip(key))
      return true;
    if (key == ZDOVars.s_SpawnPoint__DontUse)
    {
      ZDOExtraData.Set(zid, ZDOVars.s_spawnPoint, data);
      return true;
    }
    if (Mathf.Approximately(data.x, data.y) && Mathf.Approximately(data.x, data.z))
    {
      if (key == ZDOVars.s_scaleHash)
      {
        if (Mathf.Approximately(data.x, 1f))
          return true;
        ZDOExtraData.Set(zid, ZDOVars.s_scaleScalarHash, data.x);
        return true;
      }
      if (Mathf.Approximately(data.x, 0.0f))
        return true;
    }
    return false;
  }

  private static bool StripConvert(ZDOID zid, int key, float data)
  {
    return ZDO.Strip(key) || key == ZDOVars.s_scaleScalarHash && Mathf.Approximately(data, 1f);
  }

  public void LoadOldFormat(ZPackage pkg, int version)
  {
    int num1 = (int) pkg.ReadUInt();
    int num2 = (int) pkg.ReadUInt();
    this.Persistent = pkg.ReadBool();
    pkg.ReadLong();
    ZDOExtraData.SetTimeCreated(this.m_uid, pkg.ReadLong());
    pkg.ReadInt();
    if (version >= 16 /*0x10*/ && version < 24)
      pkg.ReadInt();
    if (version >= 23)
      this.Type = (ZDO.ObjectType) ((uint) pkg.ReadSByte() & 3U);
    if (version >= 22)
      this.Distant = pkg.ReadBool();
    if (version < 13)
    {
      int num3 = (int) pkg.ReadChar();
      int num4 = (int) pkg.ReadChar();
    }
    if (version >= 17)
      this.m_prefab = pkg.ReadInt();
    this.m_sector = pkg.ReadVector2i().ClampToShort();
    this.m_position = pkg.ReadVector3();
    this.m_rotation = pkg.ReadQuaternion().eulerAngles;
    int num5 = (int) pkg.ReadChar();
    if (num5 > 0)
    {
      for (int index = 0; index < num5; ++index)
      {
        int num6 = pkg.ReadInt();
        float data = pkg.ReadSingle();
        if (!ZDO.StripConvert(this.m_uid, num6, data))
          ZDOExtraData.Set(this.m_uid, num6, data);
      }
    }
    int num7 = (int) pkg.ReadChar();
    if (num7 > 0)
    {
      for (int index = 0; index < num7; ++index)
      {
        int num8 = pkg.ReadInt();
        Vector3 data = pkg.ReadVector3();
        if (!ZDO.StripConvert(this.m_uid, num8, data))
          ZDOExtraData.Set(this.m_uid, num8, data);
      }
    }
    int num9 = (int) pkg.ReadChar();
    if (num9 > 0)
    {
      for (int index = 0; index < num9; ++index)
      {
        int num10 = pkg.ReadInt();
        Quaternion quaternion = pkg.ReadQuaternion();
        if (!ZDO.Strip(num10))
          ZDOExtraData.Set(this.m_uid, num10, quaternion);
      }
    }
    int num11 = (int) pkg.ReadChar();
    if (num11 > 0)
    {
      for (int index = 0; index < num11; ++index)
      {
        int num12 = pkg.ReadInt();
        int num13 = pkg.ReadInt();
        if (!ZDO.Strip(num12))
          ZDOExtraData.Set(this.m_uid, num12, num13);
      }
    }
    int num14 = (int) pkg.ReadChar();
    if (num14 > 0)
    {
      for (int index = 0; index < num14; ++index)
      {
        int num15 = pkg.ReadInt();
        long data = pkg.ReadLong();
        if (!ZDO.StripConvert(this.m_uid, num15, data))
          ZDOExtraData.Set(this.m_uid, num15, data);
      }
    }
    int num16 = (int) pkg.ReadChar();
    if (num16 > 0)
    {
      for (int index = 0; index < num16; ++index)
      {
        int num17 = pkg.ReadInt();
        string str = pkg.ReadString();
        if (!ZDO.Strip(num17))
          ZDOExtraData.Set(this.m_uid, num17, str);
      }
    }
    if (version >= 27)
    {
      int num18 = (int) pkg.ReadChar();
      if (num18 > 0)
      {
        for (int index = 0; index < num18; ++index)
        {
          int num19 = pkg.ReadInt();
          byte[] numArray = pkg.ReadByteArray();
          if (!ZDO.Strip(num19))
            ZDOExtraData.Set(this.m_uid, num19, numArray);
        }
      }
    }
    if (version >= 17)
      return;
    this.m_prefab = this.GetInt("prefab");
  }

  private int ReadNumItems(ZPackage pkg, int version)
  {
    return version < 33 ? (int) pkg.ReadByte() : pkg.ReadNumItems();
  }

  public void Load(ZPackage pkg, int version)
  {
    this.m_uid.SetID(++ZDOID.m_loadID);
    ushort num1 = pkg.ReadUShort();
    this.Persistent = ((uint) num1 & 256U /*0x0100*/) > 0U;
    this.Distant = ((uint) num1 & 512U /*0x0200*/) > 0U;
    this.Type = (ZDO.ObjectType) ((int) num1 >> 10 & 3);
    this.m_sector = pkg.ReadVector2s();
    this.m_position = pkg.ReadVector3();
    this.m_prefab = pkg.ReadInt();
    this.OwnerRevision = (ushort) 0;
    this.DataRevision = 0U;
    this.Owned = false;
    this.Owner = false;
    this.Valid = true;
    this.SaveClone = false;
    if (((uint) num1 & 4096U /*0x1000*/) > 0U)
      this.m_rotation = pkg.ReadVector3();
    if (((int) num1 & (int) byte.MaxValue) == 0)
      return;
    int num2 = ((uint) num1 & 1U) > 0U ? 1 : 0;
    bool flag1 = ((uint) num1 & 2U) > 0U;
    bool flag2 = ((uint) num1 & 4U) > 0U;
    bool flag3 = ((uint) num1 & 8U) > 0U;
    bool flag4 = ((uint) num1 & 16U /*0x10*/) > 0U;
    bool flag5 = ((uint) num1 & 32U /*0x20*/) > 0U;
    bool flag6 = ((uint) num1 & 64U /*0x40*/) > 0U;
    bool flag7 = ((uint) num1 & 128U /*0x80*/) > 0U;
    if (num2 != 0)
      ZDOExtraData.SetConnectionData(this.m_uid, (ZDOExtraData.ConnectionType) pkg.ReadByte(), pkg.ReadInt());
    if (flag1)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Float, size);
      for (int index = 0; index < size; ++index)
      {
        int num3 = pkg.ReadInt();
        float data = pkg.ReadSingle();
        if (!ZDO.StripConvert(this.m_uid, num3, data))
          ZDOExtraData.Add(this.m_uid, num3, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.Float);
    }
    if (flag2)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Vec3, size);
      for (int index = 0; index < size; ++index)
      {
        int num4 = pkg.ReadInt();
        Vector3 data = pkg.ReadVector3();
        if (!ZDO.StripConvert(this.m_uid, num4, data))
          ZDOExtraData.Add(this.m_uid, num4, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.Vec3);
    }
    if (flag3)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Quat, size);
      for (int index = 0; index < size; ++index)
      {
        int num5 = pkg.ReadInt();
        Quaternion data = pkg.ReadQuaternion();
        if (!ZDO.Strip(num5, data))
          ZDOExtraData.Add(this.m_uid, num5, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.Quat);
    }
    if (flag4)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Int, size);
      for (int index = 0; index < size; ++index)
      {
        int num6 = pkg.ReadInt();
        int data = pkg.ReadInt();
        if (!ZDO.Strip(num6, data))
          ZDOExtraData.Add(this.m_uid, num6, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.Int);
    }
    if (flag5)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.Long, size);
      for (int index = 0; index < size; ++index)
      {
        int num7 = pkg.ReadInt();
        long data = pkg.ReadLong();
        if (!ZDO.Strip(num7, data))
          ZDOExtraData.Add(this.m_uid, num7, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.Long);
    }
    if (flag6)
    {
      int size = this.ReadNumItems(pkg, version);
      ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.String, size);
      for (int index = 0; index < size; ++index)
      {
        int num8 = pkg.ReadInt();
        string data = pkg.ReadString();
        if (!ZDO.Strip(num8, data))
          ZDOExtraData.Add(this.m_uid, num8, data);
      }
      ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.String);
    }
    if (!flag7)
      return;
    int size1 = this.ReadNumItems(pkg, version);
    ZDOExtraData.Reserve(this.m_uid, ZDOExtraData.Type.ByteArray, size1);
    for (int index = 0; index < size1; ++index)
    {
      int num9 = pkg.ReadInt();
      byte[] data = pkg.ReadByteArray();
      if (!ZDO.Strip(num9, data))
        ZDOExtraData.Add(this.m_uid, num9, data);
    }
    ZDOExtraData.RemoveIfEmpty(this.m_uid, ZDOExtraData.Type.ByteArray);
  }

  public long GetOwner() => !this.Owned ? 0L : ZDOExtraData.GetOwner(this.m_uid);

  public bool IsOwner() => this.Owner;

  public bool HasOwner() => this.Owned;

  public void SetOwner(long uid)
  {
    if (ZDOExtraData.GetOwner(this.m_uid) == uid)
      return;
    this.SetOwnerInternal(uid);
    this.IncreaseOwnerRevision();
  }

  public void SetOwnerInternal(long uid)
  {
    if (uid == 0L)
    {
      ZDOExtraData.ReleaseOwner(this.m_uid);
      this.Owned = false;
      this.Owner = false;
    }
    else
    {
      ZDOExtraData.SetOwner(this.m_uid, ZDOID.AddUser(uid));
      this.Owned = true;
      this.Owner = uid == ZDOMan.GetSessionID();
    }
  }

  public bool Persistent
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Persistent) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Persistent;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Distant | ZDO.DataFlags.Created | ZDO.DataFlags.Owner | ZDO.DataFlags.Owned | ZDO.DataFlags.Valid;
    }
  }

  public bool Distant
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Distant) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Distant;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Persistent | ZDO.DataFlags.Created | ZDO.DataFlags.Owner | ZDO.DataFlags.Owned | ZDO.DataFlags.Valid;
    }
  }

  private bool Owner
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Owner) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Owner;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Persistent | ZDO.DataFlags.Distant | ZDO.DataFlags.Created | ZDO.DataFlags.Owned | ZDO.DataFlags.Valid;
    }
  }

  private bool Owned
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Owned) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Owned;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Persistent | ZDO.DataFlags.Distant | ZDO.DataFlags.Created | ZDO.DataFlags.Owner | ZDO.DataFlags.Valid;
    }
  }

  private bool Valid
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Valid) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Valid;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Persistent | ZDO.DataFlags.Distant | ZDO.DataFlags.Created | ZDO.DataFlags.Owner | ZDO.DataFlags.Owned;
    }
  }

  public bool Created
  {
    get => (this.m_dataFlags & ZDO.DataFlags.Created) != 0;
    set
    {
      if (value)
        this.m_dataFlags |= ZDO.DataFlags.Created;
      else
        this.m_dataFlags &= ZDO.DataFlags.Type | ZDO.DataFlags.Persistent | ZDO.DataFlags.Distant | ZDO.DataFlags.Owner | ZDO.DataFlags.Owned | ZDO.DataFlags.Valid;
    }
  }

  public ZDO.ObjectType Type
  {
    get => (ZDO.ObjectType) (this.m_dataFlags & ZDO.DataFlags.Type);
    set
    {
      this.m_dataFlags = this.m_dataFlags & (ZDO.DataFlags.Persistent | ZDO.DataFlags.Distant | ZDO.DataFlags.Created | ZDO.DataFlags.Owner | ZDO.DataFlags.Owned | ZDO.DataFlags.Valid) | (ZDO.DataFlags) (value & ZDO.ObjectType.Terrain);
    }
  }

  private bool SaveClone
  {
    get => (double) this.m_tempSortValue < 0.0;
    set
    {
      if (value)
        this.m_tempSortValue = -1f;
      else
        this.m_tempSortValue = 0.0f;
    }
  }

  public byte TempRemoveEarmark
  {
    get => this.m_tempRemoveEarmark;
    set => this.m_tempRemoveEarmark = value;
  }

  public ushort OwnerRevision { get; set; }

  public uint DataRevision { get; set; }

  [Flags]
  private enum DataFlags : byte
  {
    None = 0,
    Type = 3,
    Persistent = 4,
    Distant = 8,
    Created = 16, // 0x10
    Owner = 32, // 0x20
    Owned = 64, // 0x40
    Valid = 128, // 0x80
  }

  public enum ObjectType : byte
  {
    Default,
    Prioritized,
    Solid,
    Terrain,
  }
}
