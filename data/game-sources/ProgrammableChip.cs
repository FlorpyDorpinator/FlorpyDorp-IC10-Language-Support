using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Inventory;
using Assets.Scripts.Localization2;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.CompilerServices;
using Objects.Electrical;
using Objects.Rockets;
using Reagents;
using Trading;
using UnityEngine;

namespace Assets.Scripts.Objects.Electrical
{
	// Token: 0x02000C9C RID: 3228
	public class ProgrammableChip : Item, ISourceCode, IReferencable, IEvaluable, ISpatial, IPhysical, IProfile, IDensePoolable, ILogicable, IMemoryWritable, IMemory, IMemoryReadable
	{
		// Token: 0x06006F0C RID: 28428 RVA: 0x00214AD7 File Offset: 0x00212CD7
		public override string GetStationpediaCategory()
		{
			return Localization.GetInterface(StationpediaCategoryStrings.LogicIntegratedCircuitsCategory, false);
		}

		// Token: 0x06006F0D RID: 28429 RVA: 0x00214AE4 File Offset: 0x00212CE4
		public static string UnpackAscii6(double packedDouble, bool signed)
		{
			ulong num = (ulong)ProgrammableChip.DoubleToLong(packedDouble, signed);
			int num2 = 0;
			ulong num3 = num;
			while (num3 != 0UL && num2 < 8)
			{
				num2++;
				num3 >>= 8;
			}
			char[] array = new char[num2];
			for (int i = num2 - 1; i >= 0; i--)
			{
				array[i] = (char)(num & 255UL);
				num >>= 8;
			}
			return new string(array);
		}

		// Token: 0x06006F0E RID: 28430 RVA: 0x00214B40 File Offset: 0x00212D40
		public static double PackAscii6(string text, int lineNumber)
		{
			if (string.IsNullOrEmpty(text))
			{
				throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidStringNull, lineNumber);
			}
			if (text.Length > 6)
			{
				throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidStringLength, lineNumber);
			}
			long num = 0L;
			foreach (char c in text)
			{
				if (c > '\u007f')
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidStringNonAscii, lineNumber);
				}
				num = (num << 8 | (long)((ulong)((byte)c)));
			}
			return (double)num;
		}

		// Token: 0x06006F0F RID: 28431 RVA: 0x00214BA6 File Offset: 0x00212DA6
		public override bool CanLogicRead(LogicType logicType)
		{
			return logicType == LogicType.LineNumber || base.CanLogicRead(logicType);
		}

		// Token: 0x06006F10 RID: 28432 RVA: 0x00214BB9 File Offset: 0x00212DB9
		public override double GetLogicValue(LogicType logicType)
		{
			if (logicType == LogicType.LineNumber)
			{
				return (double)this._NextAddr;
			}
			return base.GetLogicValue(logicType);
		}

		// Token: 0x1700137F RID: 4991
		// (get) Token: 0x06006F12 RID: 28434 RVA: 0x002150AA File Offset: 0x002132AA
		// (set) Token: 0x06006F13 RID: 28435 RVA: 0x002150B2 File Offset: 0x002132B2
		public char[] SourceCodeCharArray { get; set; }

		// Token: 0x17001380 RID: 4992
		// (get) Token: 0x06006F14 RID: 28436 RVA: 0x002150BB File Offset: 0x002132BB
		// (set) Token: 0x06006F15 RID: 28437 RVA: 0x002150C3 File Offset: 0x002132C3
		public int SourceCodeWritePointer { get; set; }

		// Token: 0x17001381 RID: 4993
		// (get) Token: 0x06006F16 RID: 28438 RVA: 0x002150CC File Offset: 0x002132CC
		public string ErrorLineNumberString
		{
			get
			{
				return StringManager.Get((int)this._ErrorLineNumberSynced);
			}
		}

		// Token: 0x17001382 RID: 4994
		// (get) Token: 0x06006F17 RID: 28439 RVA: 0x002150D9 File Offset: 0x002132D9
		// (set) Token: 0x06006F18 RID: 28440 RVA: 0x002150E1 File Offset: 0x002132E1
		public ulong LastEditedId { get; set; }

		// Token: 0x17001383 RID: 4995
		// (get) Token: 0x06006F19 RID: 28441 RVA: 0x002150EA File Offset: 0x002132EA
		// (set) Token: 0x06006F1A RID: 28442 RVA: 0x002150F2 File Offset: 0x002132F2
		private ushort _ErrorLineNumber
		{
			get
			{
				return this._ErrorLineNumberSynced;
			}
			set
			{
				if (this._ErrorLineNumberSynced != value)
				{
					this._ErrorLineNumberSynced = value;
					if (NetworkManager.IsServer)
					{
						base.NetworkUpdateFlags |= 512;
					}
				}
			}
		}

		// Token: 0x17001384 RID: 4996
		// (get) Token: 0x06006F1B RID: 28443 RVA: 0x0021511E File Offset: 0x0021331E
		public string ErrorTypeString
		{
			get
			{
				return ProgrammableChip._exceptionTypes.GetName(this._ErrorType, false);
			}
		}

		// Token: 0x17001385 RID: 4997
		// (get) Token: 0x06006F1C RID: 28444 RVA: 0x00215131 File Offset: 0x00213331
		// (set) Token: 0x06006F1D RID: 28445 RVA: 0x0021513C File Offset: 0x0021333C
		private ProgrammableChipException.ICExceptionType _ErrorType
		{
			get
			{
				return (ProgrammableChipException.ICExceptionType)this._ErrorTypeSynced;
			}
			set
			{
				if (value != (ProgrammableChipException.ICExceptionType)this._ErrorTypeSynced)
				{
					this._ErrorTypeSynced = (byte)value;
					if (NetworkManager.IsServer)
					{
						base.NetworkUpdateFlags |= 512;
					}
				}
			}
		}

		// Token: 0x17001386 RID: 4998
		// (get) Token: 0x06006F1E RID: 28446 RVA: 0x00215175 File Offset: 0x00213375
		public bool CompilationError
		{
			get
			{
				return this.CompileErrorType != ProgrammableChipException.ICExceptionType.None;
			}
		}

		// Token: 0x17001387 RID: 4999
		// (get) Token: 0x06006F1F RID: 28447 RVA: 0x00215183 File Offset: 0x00213383
		// (set) Token: 0x06006F20 RID: 28448 RVA: 0x0021518B File Offset: 0x0021338B
		private ushort CompileErrorLineNumber
		{
			get
			{
				return this._compileErrorLineNumber;
			}
			set
			{
				this._compileErrorLineNumber = value;
				if (NetworkManager.IsServer)
				{
					base.NetworkUpdateFlags |= 512;
				}
			}
		}

		// Token: 0x17001388 RID: 5000
		// (get) Token: 0x06006F21 RID: 28449 RVA: 0x002151AE File Offset: 0x002133AE
		// (set) Token: 0x06006F22 RID: 28450 RVA: 0x002151B6 File Offset: 0x002133B6
		private ProgrammableChipException.ICExceptionType CompileErrorType
		{
			get
			{
				return this._compileErrorType;
			}
			set
			{
				this._compileErrorType = value;
				if (NetworkManager.IsServer)
				{
					base.NetworkUpdateFlags |= 512;
				}
			}
		}

		// Token: 0x17001389 RID: 5001
		// (get) Token: 0x06006F23 RID: 28451 RVA: 0x002151D9 File Offset: 0x002133D9
		private ICircuitHolder CircuitHousing
		{
			get
			{
				Slot parentSlot = base.ParentSlot;
				return ((parentSlot != null) ? parentSlot.Parent : null) as ICircuitHolder;
			}
		}

		// Token: 0x06006F24 RID: 28452 RVA: 0x002151F4 File Offset: 0x002133F4
		public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType)
		{
			base.BuildUpdate(writer, networkUpdateType);
			if (Thing.IsNetworkUpdateRequired(512U, networkUpdateType))
			{
				writer.WriteUInt16(this._ErrorLineNumber);
				writer.WriteByte((byte)this._ErrorType);
				writer.WriteUInt16(this.CompileErrorLineNumber);
				writer.WriteByte((byte)this.CompileErrorType);
			}
			if (Thing.IsNetworkUpdateRequired(256U, networkUpdateType))
			{
				writer.WriteAscii(this.SourceCode);
			}
		}

		// Token: 0x06006F25 RID: 28453 RVA: 0x00215260 File Offset: 0x00213460
		public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType)
		{
			base.ProcessUpdate(reader, networkUpdateType);
			if (Thing.IsNetworkUpdateRequired(512U, networkUpdateType))
			{
				this._ErrorLineNumber = reader.ReadUInt16();
				this._ErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
				this.CompileErrorLineNumber = reader.ReadUInt16();
				this.CompileErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
			}
			if (Thing.IsNetworkUpdateRequired(256U, networkUpdateType))
			{
				this.SetSourceCode(new string(reader.ReadChars()));
			}
		}

		// Token: 0x06006F26 RID: 28454 RVA: 0x002152D0 File Offset: 0x002134D0
		public override void SerializeOnJoin(RocketBinaryWriter writer)
		{
			base.SerializeOnJoin(writer);
			writer.WriteUInt16(this._ErrorLineNumber);
			writer.WriteByte((byte)this._ErrorType);
			writer.WriteUInt16(this.CompileErrorLineNumber);
			writer.WriteByte((byte)this.CompileErrorType);
			writer.WriteAscii(this.SourceCode);
		}

		// Token: 0x06006F27 RID: 28455 RVA: 0x00215320 File Offset: 0x00213520
		public override void DeserializeOnJoin(RocketBinaryReader reader)
		{
			base.DeserializeOnJoin(reader);
			this._ErrorLineNumber = reader.ReadUInt16();
			this._ErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
			this.CompileErrorLineNumber = reader.ReadUInt16();
			this.CompileErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
			this.SetSourceCode(new string(reader.ReadChars()));
		}

		// Token: 0x06006F28 RID: 28456 RVA: 0x00215375 File Offset: 0x00213575
		public void SendUpdate()
		{
			if (NetworkManager.IsClient)
			{
				ISourceCode.SendSourceCodeToServer(this.SourceCode, base.ReferenceId);
				return;
			}
			if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 256;
			}
		}

		// Token: 0x1700138A RID: 5002
		// (get) Token: 0x06006F29 RID: 28457 RVA: 0x002153AA File Offset: 0x002135AA
		public float MemoryUsed
		{
			get
			{
				return (float)this.SourceCode.Length;
			}
		}

		// Token: 0x06006F2A RID: 28458 RVA: 0x002153B8 File Offset: 0x002135B8
		public override string GetQuantityText()
		{
			return StringGenerator.GetString((int)this.MemoryUsed, Unit.ProgrammableChip);
		}

		// Token: 0x06006F2B RID: 28459 RVA: 0x002153C7 File Offset: 0x002135C7
		public void SetSourceCode(string sourceCode, ICircuitHolder parent)
		{
			parent.ClearError();
			this.SetSourceCode(sourceCode);
		}

		// Token: 0x06006F2C RID: 28460 RVA: 0x002153D8 File Offset: 0x002135D8
		public void SetSourceCode(string sourceCode)
		{
			this._LinesOfCode.Clear();
			this._Aliases.Clear();
			this._Defines.Clear();
			this._JumpTags.Clear();
			this._ErrorType = ProgrammableChipException.ICExceptionType.None;
			this._ErrorLineNumber = 0;
			this.CompileErrorType = ProgrammableChipException.ICExceptionType.None;
			this.CompileErrorLineNumber = 0;
			this._Registers[this._StackPointerIndex] = 0.0;
			if (string.IsNullOrEmpty(sourceCode))
			{
				sourceCode = string.Empty;
			}
			this.SourceCode = new AsciiString(sourceCode);
			if (this.CircuitHousing != null)
			{
				this.CircuitHousing.ClearError();
				new ProgrammableChip._ALIAS_Operation(this, 0, "db", string.Format("d{0}", int.MaxValue)).Execute(0, false);
				new ProgrammableChip._ALIAS_Operation(this, 0, "sp", string.Format("r{0}", this._StackPointerIndex)).Execute(0);
				new ProgrammableChip._ALIAS_Operation(this, 0, "ra", string.Format("r{0}", this._ReturnAddressIndex)).Execute(0);
			}
			string[] array = sourceCode.Split('\n', StringSplitOptions.None);
			for (int i = 0; i < array.Length; i++)
			{
				try
				{
					if (array[i].IndexOf('#') == 0)
					{
						this._LinesOfCode.Add(new ProgrammableChip._LineOfCode(this, string.Empty, i));
					}
					else
					{
						this._LinesOfCode.Add(new ProgrammableChip._LineOfCode(this, array[i], i));
					}
				}
				catch (ProgrammableChipException ex)
				{
					ICircuitHolder circuitHousing = this.CircuitHousing;
					if (circuitHousing != null)
					{
						circuitHousing.RaiseError(1);
					}
					this.CompileErrorLineNumber = ex.LineNumber;
					this.CompileErrorType = ex.ExceptionType;
					break;
				}
				catch (Exception)
				{
					ICircuitHolder circuitHousing2 = this.CircuitHousing;
					if (circuitHousing2 != null)
					{
						circuitHousing2.RaiseError(1);
					}
					this.CompileErrorLineNumber = (ushort)i;
					this.CompileErrorType = ProgrammableChipException.ICExceptionType.Unknown;
					break;
				}
			}
			this._NextAddr = 0;
		}

		// Token: 0x06006F2D RID: 28461 RVA: 0x002155BC File Offset: 0x002137BC
		public AsciiString GetSourceCode()
		{
			return this.SourceCode;
		}

		// Token: 0x06006F2E RID: 28462 RVA: 0x002155C4 File Offset: 0x002137C4
		public override Thing.DelayedActionInstance AttackWith(Attack attack, bool doAction = true)
		{
			Labeller labeller = attack.SourceItem as Labeller;
			if (labeller == null)
			{
				return base.AttackWith(attack, doAction);
			}
			Thing.DelayedActionInstance delayedActionInstance = new Thing.DelayedActionInstance
			{
				Duration = 0f,
				ActionMessage = ActionStrings.Rename
			};
			if (!labeller.OnOff)
			{
				return delayedActionInstance.Fail(GameStrings.DeviceNotOn);
			}
			if (!labeller.IsOperable)
			{
				return delayedActionInstance.Fail(GameStrings.DeviceNoPower);
			}
			if (!doAction)
			{
				return delayedActionInstance;
			}
			labeller.Rename(this);
			return delayedActionInstance;
		}

		// Token: 0x06006F2F RID: 28463 RVA: 0x0021563C File Offset: 0x0021383C
		public void Execute(int runCount)
		{
			if (this._NextAddr < 0 || this._NextAddr >= this._LinesOfCode.Count || this._LinesOfCode.Count == 0)
			{
				return;
			}
			int nextAddr = this._NextAddr;
			int num = runCount;
			while (num-- > 0 && this._NextAddr >= 0 && this._NextAddr < this._LinesOfCode.Count)
			{
				nextAddr = this._NextAddr;
				try
				{
					ProgrammableChip._Operation operation = this._LinesOfCode[this._NextAddr].Operation;
					this._NextAddr = operation.Execute(this._NextAddr);
				}
				catch (ProgrammableChipException ex)
				{
					ICircuitHolder circuitHousing = this.CircuitHousing;
					if (circuitHousing != null)
					{
						circuitHousing.RaiseError(1);
					}
					this._ErrorLineNumber = ex.LineNumber;
					this._ErrorType = ex.ExceptionType;
					this._NextAddr = nextAddr;
					break;
				}
				catch (Exception)
				{
					if (this.CircuitHousing != null)
					{
						this.CircuitHousing.RaiseError(1);
					}
					this._ErrorLineNumber = (ushort)nextAddr;
					this._ErrorType = ProgrammableChipException.ICExceptionType.Unknown;
					this._NextAddr = nextAddr;
					break;
				}
				if (this.CircuitHousing != null)
				{
					this._ErrorLineNumber = 0;
					this._ErrorType = ProgrammableChipException.ICExceptionType.None;
					this.CircuitHousing.RaiseError(0);
				}
				if (this._NextAddr < 0)
				{
					this._NextAddr = -this._NextAddr;
					return;
				}
			}
		}

		// Token: 0x06006F30 RID: 28464 RVA: 0x00215794 File Offset: 0x00213994
		public override ThingSaveData SerializeSave()
		{
			ThingSaveData thingSaveData;
			ThingSaveData result = thingSaveData = new ProgrammableChipSaveData();
			this.InitialiseSaveData(ref thingSaveData);
			return result;
		}

		// Token: 0x06006F31 RID: 28465 RVA: 0x002157B0 File Offset: 0x002139B0
		public override void DeserializeSave(ThingSaveData savedData)
		{
			base.DeserializeSave(savedData);
			ProgrammableChipSaveData programmableChipSaveData = savedData as ProgrammableChipSaveData;
			if (programmableChipSaveData != null)
			{
				this.SetSourceCode(programmableChipSaveData.SourceCode);
				for (int i = 0; i < programmableChipSaveData.Registers.Length; i++)
				{
					this._Registers[i] = programmableChipSaveData.Registers[i];
				}
				this._NextAddr = programmableChipSaveData.NextAddr;
				this._Aliases.Clear();
				if (programmableChipSaveData.NewAliasesKeys != null)
				{
					for (int j = 0; j < programmableChipSaveData.NewAliasesKeys.Length; j++)
					{
						ProgrammableChip._AliasValue value = new ProgrammableChip._AliasValue((ProgrammableChip._AliasTarget)programmableChipSaveData.NewAliasesValuesTarget[j], programmableChipSaveData.NewAliasesValuesIndex[j]);
						this._Aliases.Add(programmableChipSaveData.NewAliasesKeys[j], value);
					}
				}
				if (programmableChipSaveData.DeviceLables != null)
				{
					for (int k = 0; k < programmableChipSaveData.DeviceLables.Count; k++)
					{
						this._Aliases.Add(programmableChipSaveData.DeviceLables[k], new ProgrammableChip._AliasValue(ProgrammableChip._AliasTarget.Device, k));
					}
				}
				if (programmableChipSaveData.AliasesValues != null)
				{
					for (int l = 0; l < programmableChipSaveData.AliasesValues.Length; l++)
					{
						this._Aliases.Add(programmableChipSaveData.AliasesKeys[l], new ProgrammableChip._AliasValue(ProgrammableChip._AliasTarget.Register, programmableChipSaveData.AliasesValues[l]));
					}
				}
				this._JumpTags.Clear();
				if (programmableChipSaveData.JumpTagsKeys != null)
				{
					for (int m = 0; m < programmableChipSaveData.JumpTagsKeys.Length; m++)
					{
						this._JumpTags.Add(programmableChipSaveData.JumpTagsKeys[m], programmableChipSaveData.JumpTagsValues[m]);
					}
				}
				if (programmableChipSaveData.Stack != null)
				{
					int num = 0;
					while (num < this._Stack.Length && num < programmableChipSaveData.Stack.Length)
					{
						this._Stack[num] = programmableChipSaveData.Stack[num];
						num++;
					}
				}
				this._Defines.Clear();
				if (programmableChipSaveData.DefineKeys != null)
				{
					for (int n = 0; n < programmableChipSaveData.DefineKeys.Length; n++)
					{
						this._Defines.Add(programmableChipSaveData.DefineKeys[n], programmableChipSaveData.DefineValues[n]);
					}
				}
				if (this._NextAddr >= 0 && this._NextAddr < this._LinesOfCode.Count && this._LinesOfCode[this._NextAddr].Operation is ProgrammableChip._SLEEP_Operation)
				{
					ProgrammableChip._SLEEP_Operation sleep_Operation = (ProgrammableChip._SLEEP_Operation)this._LinesOfCode[this._NextAddr].Operation;
					sleep_Operation.LastTimeSet = GameManager.GameTime;
					sleep_Operation.SleepDurationRemaining = programmableChipSaveData.SleepDurationRemaining;
				}
			}
		}

		// Token: 0x06006F32 RID: 28466 RVA: 0x00215A18 File Offset: 0x00213C18
		protected override void InitialiseSaveData(ref ThingSaveData savedData)
		{
			base.InitialiseSaveData(ref savedData);
			ProgrammableChipSaveData programmableChipSaveData = savedData as ProgrammableChipSaveData;
			if (programmableChipSaveData == null)
			{
				return;
			}
			programmableChipSaveData.Registers = new double[this._Registers.Length];
			for (int i = 0; i < this._Registers.Length; i++)
			{
				programmableChipSaveData.Registers[i] = this._Registers[i];
			}
			programmableChipSaveData.SourceCode = this.GetSourceCode().ToString();
			programmableChipSaveData.NextAddr = this._NextAddr;
			programmableChipSaveData.NewAliasesKeys = new string[this._Aliases.Count];
			programmableChipSaveData.NewAliasesValuesTarget = new int[this._Aliases.Count];
			programmableChipSaveData.NewAliasesValuesIndex = new int[this._Aliases.Count];
			int num = 0;
			foreach (KeyValuePair<string, ProgrammableChip._AliasValue> keyValuePair in this._Aliases)
			{
				programmableChipSaveData.NewAliasesKeys[num] = keyValuePair.Key;
				programmableChipSaveData.NewAliasesValuesTarget[num] = (int)keyValuePair.Value.Target;
				programmableChipSaveData.NewAliasesValuesIndex[num] = keyValuePair.Value.Index;
				num++;
			}
			programmableChipSaveData.JumpTagsKeys = new string[this._JumpTags.Count];
			programmableChipSaveData.JumpTagsValues = new int[this._JumpTags.Count];
			num = 0;
			foreach (KeyValuePair<string, int> keyValuePair2 in this._JumpTags)
			{
				programmableChipSaveData.JumpTagsKeys[num] = keyValuePair2.Key;
				programmableChipSaveData.JumpTagsValues[num] = keyValuePair2.Value;
				num++;
			}
			programmableChipSaveData.DefineKeys = new string[this._Defines.Count];
			programmableChipSaveData.DefineValues = new double[this._Defines.Count];
			num = 0;
			foreach (KeyValuePair<string, double> keyValuePair3 in this._Defines)
			{
				programmableChipSaveData.DefineKeys[num] = keyValuePair3.Key;
				programmableChipSaveData.DefineValues[num] = keyValuePair3.Value;
				num++;
			}
			int num2 = this._Stack.Length;
			int num3 = this._Stack.Length;
			while (num3 > 0 && this._Stack[num3 - 1] == 0.0)
			{
				num2--;
				num3--;
			}
			programmableChipSaveData.Stack = new double[num2];
			for (int j = 0; j < num2; j++)
			{
				programmableChipSaveData.Stack[j] = this._Stack[j];
			}
			if (this._NextAddr >= 0 && this._NextAddr < this._LinesOfCode.Count && this._LinesOfCode[this._NextAddr].Operation is ProgrammableChip._SLEEP_Operation)
			{
				ProgrammableChip._SLEEP_Operation sleep_Operation = (ProgrammableChip._SLEEP_Operation)this._LinesOfCode[this._NextAddr].Operation;
				programmableChipSaveData.SleepDurationRemaining = sleep_Operation.SleepDurationRemaining;
			}
		}

		// Token: 0x06006F33 RID: 28467 RVA: 0x00215D3C File Offset: 0x00213F3C
		public static string GetIntroString()
		{
			return string.Format("These functions are generally typed per line of instruction to your Integrated Circuit (IC). Below are a list of the functions. {0} refers to a device, the ? character replaced with either the screw number, or 'b' for base unit. For example 'd0' or 'db'. {1} refers to a register, the ? refers to the number of the register, such as 'r0'. Additional r in front allows indirect referencing. {2} refers to a logic variable, whether slot or general.", "<color=green>d?</color>", "<color=#0080FFFF>r?</color>", "<color=orange>var</color>");
		}

		// Token: 0x06006F34 RID: 28468 RVA: 0x00215D58 File Offset: 0x00213F58
		private static string MakeString(ScriptCommand command, string color, int paramCount, params ProgrammableChip.HelpString[] strings)
		{
			bool flag = paramCount >= 0;
			string text = flag ? string.Empty : string.Concat(new string[]
			{
				"<color=",
				color,
				">",
				EnumCollections.ScriptCommands.GetName(command, false),
				"</color>"
			});
			if (paramCount < 0)
			{
				paramCount = 0;
			}
			for (int i = paramCount; i < strings.Length; i++)
			{
				text = text + " " + strings[i].ToString();
			}
			if (flag)
			{
				string pattern = "<color\\s*=\\s*['\"]?(?<color>[^'\">]+)['\"]?\\s*>(?<content>.*?)<\\/color>";
				string replacement = "<color=" + color + ">${content}</color>";
				text = Regex.Replace(text, pattern, replacement);
			}
			return text;
		}

		// Token: 0x06006F35 RID: 28469 RVA: 0x00215E04 File Offset: 0x00214004
		public static string SetString(string format, string command)
		{
			return string.Format(format, new object[]
			{
				string.Format("<color=yellow>{0}</color>", command),
				"<color=green>d?</color>",
				"<color=orange>var</color>",
				"<color=white>num</color>",
				"<color=white>int</color>",
				"<color=#585858FF>|</color>",
				"<color=#0080FFFF>r?</color>",
				"<color=white>str</color>",
				"<color=#0080FFFF>r?</color>|<color=white>num</color>",
				"<color=#0080FFFF>r?</color>|<color=green>d?</color>",
				"<color=orange>reagentMode</color>",
				"<color=white>reagent</color>",
				"<color=white>type</color>",
				"<color=orange>batchMode</color>",
				"<color=lightblue>...</color>"
			});
		}

		// Token: 0x06006F36 RID: 28470 RVA: 0x00215EA2 File Offset: 0x002140A2
		public static string SetString(string format, ScriptCommand command)
		{
			return ProgrammableChip.SetString(format, command.ToString());
		}

		// Token: 0x06006F37 RID: 28471 RVA: 0x00215EB8 File Offset: 0x002140B8
		public static string GetCommandExample(ScriptCommand command, string color = "yellow", int spaceCount = -1)
		{
			switch (command)
			{
			case ScriptCommand.l:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.LOGIC_TYPE
				});
			case ScriptCommand.s:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.ls:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.SLOT_INDEX,
					ProgrammableChip.LOGIC_SLOT_TYPE
				});
			case ScriptCommand.lr:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.REAGENT_MODE,
					ProgrammableChip.INTEGER
				});
			case ScriptCommand.sb:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.lb:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.BATCH_MODE
				});
			case ScriptCommand.alias:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.STRING,
					ProgrammableChip.REGISTER + ProgrammableChip.DEVICE_INDEX
				});
			case ScriptCommand.move:
			case ScriptCommand.sqrt:
			case ScriptCommand.round:
			case ScriptCommand.trunc:
			case ScriptCommand.ceil:
			case ScriptCommand.floor:
			case ScriptCommand.abs:
			case ScriptCommand.log:
			case ScriptCommand.exp:
			case ScriptCommand.sltz:
			case ScriptCommand.sgtz:
			case ScriptCommand.slez:
			case ScriptCommand.sgez:
			case ScriptCommand.seqz:
			case ScriptCommand.snez:
			case ScriptCommand.sin:
			case ScriptCommand.asin:
			case ScriptCommand.tan:
			case ScriptCommand.atan:
			case ScriptCommand.cos:
			case ScriptCommand.acos:
			case ScriptCommand.snan:
			case ScriptCommand.snanz:
			case ScriptCommand.not:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a")
				});
			case ScriptCommand.add:
			case ScriptCommand.sub:
			case ScriptCommand.slt:
			case ScriptCommand.sgt:
			case ScriptCommand.sle:
			case ScriptCommand.sge:
			case ScriptCommand.seq:
			case ScriptCommand.sne:
			case ScriptCommand.and:
			case ScriptCommand.or:
			case ScriptCommand.xor:
			case ScriptCommand.nor:
			case ScriptCommand.mul:
			case ScriptCommand.div:
			case ScriptCommand.mod:
			case ScriptCommand.max:
			case ScriptCommand.min:
			case ScriptCommand.sapz:
			case ScriptCommand.snaz:
			case ScriptCommand.atan2:
			case ScriptCommand.srl:
			case ScriptCommand.sra:
			case ScriptCommand.sll:
			case ScriptCommand.sla:
			case ScriptCommand.pow:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b")
				});
			case ScriptCommand.sdse:
			case ScriptCommand.sdns:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device")
				});
			case ScriptCommand.sap:
			case ScriptCommand.sna:
			case ScriptCommand.select:
			case ScriptCommand.ext:
			case ScriptCommand.ins:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("c")
				});
			case ScriptCommand.j:
			case ScriptCommand.jal:
			case ScriptCommand.jr:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.INTEGER
				});
			case ScriptCommand.bltz:
			case ScriptCommand.bgez:
			case ScriptCommand.blez:
			case ScriptCommand.bgtz:
			case ScriptCommand.bltzal:
			case ScriptCommand.bgezal:
			case ScriptCommand.blezal:
			case ScriptCommand.bgtzal:
			case ScriptCommand.brltz:
			case ScriptCommand.brgez:
			case ScriptCommand.brlez:
			case ScriptCommand.brgtz:
			case ScriptCommand.beqz:
			case ScriptCommand.bnez:
			case ScriptCommand.breqz:
			case ScriptCommand.brnez:
			case ScriptCommand.beqzal:
			case ScriptCommand.bnezal:
			case ScriptCommand.brnan:
			case ScriptCommand.bnan:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b")
				});
			case ScriptCommand.bdse:
			case ScriptCommand.bdns:
			case ScriptCommand.brdse:
			case ScriptCommand.brdns:
			case ScriptCommand.bdseal:
			case ScriptCommand.bdnsal:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a")
				});
			case ScriptCommand.beq:
			case ScriptCommand.bne:
			case ScriptCommand.beqal:
			case ScriptCommand.bneal:
			case ScriptCommand.breq:
			case ScriptCommand.brne:
			case ScriptCommand.blt:
			case ScriptCommand.bgt:
			case ScriptCommand.ble:
			case ScriptCommand.bge:
			case ScriptCommand.brlt:
			case ScriptCommand.brgt:
			case ScriptCommand.brle:
			case ScriptCommand.brge:
			case ScriptCommand.bltal:
			case ScriptCommand.bgtal:
			case ScriptCommand.bleal:
			case ScriptCommand.bgeal:
			case ScriptCommand.bapz:
			case ScriptCommand.bnaz:
			case ScriptCommand.brapz:
			case ScriptCommand.brnaz:
			case ScriptCommand.bapzal:
			case ScriptCommand.bnazal:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("c")
				});
			case ScriptCommand.bap:
			case ScriptCommand.bna:
			case ScriptCommand.brap:
			case ScriptCommand.brna:
			case ScriptCommand.bapal:
			case ScriptCommand.bnaal:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("c"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("d")
				});
			case ScriptCommand.rand:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.yield:
			case ScriptCommand.hcf:
				return ProgrammableChip.MakeString(command, color, spaceCount, Array.Empty<ProgrammableChip.HelpString>());
			case ScriptCommand.label:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.DEVICE_INDEX,
					ProgrammableChip.STRING
				});
			case ScriptCommand.peek:
			case ScriptCommand.pop:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.push:
			case ScriptCommand.sleep:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a")
				});
			case ScriptCommand.define:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.STRING,
					ProgrammableChip.NUMBER
				});
			case ScriptCommand.lbs:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.SLOT_INDEX,
					ProgrammableChip.LOGIC_SLOT_TYPE,
					ProgrammableChip.BATCH_MODE
				});
			case ScriptCommand.lbn:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.NAME_HASH,
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.BATCH_MODE
				});
			case ScriptCommand.sbn:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.NAME_HASH,
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.lbns:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.NAME_HASH,
					ProgrammableChip.SLOT_INDEX,
					ProgrammableChip.LOGIC_SLOT_TYPE,
					ProgrammableChip.BATCH_MODE
				});
			case ScriptCommand.ss:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.SLOT_INDEX,
					ProgrammableChip.LOGIC_SLOT_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.sbs:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.DEVICE_HASH,
					ProgrammableChip.SLOT_INDEX,
					ProgrammableChip.LOGIC_SLOT_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.ld:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("id"),
					ProgrammableChip.LOGIC_TYPE
				});
			case ScriptCommand.sd:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("id"),
					ProgrammableChip.LOGIC_TYPE,
					ProgrammableChip.REGISTER
				});
			case ScriptCommand.poke:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("address"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("value")
				});
			case ScriptCommand.getd:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("id"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("address")
				});
			case ScriptCommand.putd:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("id"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("address"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("value")
				});
			case ScriptCommand.get:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("address")
				});
			case ScriptCommand.put:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("address"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("value")
				});
			case ScriptCommand.clr:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.DEVICE_INDEX
				});
			case ScriptCommand.clrd:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("id")
				});
			case ScriptCommand.rmap:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					ProgrammableChip.DEVICE_INDEX,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("reagentHash")
				});
			case ScriptCommand.bdnvl:
			case ScriptCommand.bdnvs:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					(ProgrammableChip.DEVICE_INDEX + ProgrammableChip.REGISTER + ProgrammableChip.REF_ID).Var("device"),
					ProgrammableChip.LOGIC_TYPE,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a")
				});
			case ScriptCommand.lerp:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("b"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("c")
				});
			default:
				throw new ArgumentOutOfRangeException(Localization.GetInterface("ScriptCommandCommand", false), command, null);
			}
		}

		// Token: 0x06006F38 RID: 28472 RVA: 0x00216D5C File Offset: 0x00214F5C
		private UniTaskVoid HaltAndCatchFireFromThread()
		{
			ProgrammableChip.<HaltAndCatchFireFromThread>d__125 <HaltAndCatchFireFromThread>d__;
			<HaltAndCatchFireFromThread>d__.<>t__builder = AsyncUniTaskVoidMethodBuilder.Create();
			<HaltAndCatchFireFromThread>d__.<>4__this = this;
			<HaltAndCatchFireFromThread>d__.<>1__state = -1;
			<HaltAndCatchFireFromThread>d__.<>t__builder.Start<ProgrammableChip.<HaltAndCatchFireFromThread>d__125>(ref <HaltAndCatchFireFromThread>d__);
			return <HaltAndCatchFireFromThread>d__.<>t__builder.Task;
		}

		// Token: 0x06006F39 RID: 28473 RVA: 0x00216DA0 File Offset: 0x00214FA0
		private void HaltAndCatchFire()
		{
			if (GameManager.IsThread)
			{
				this.HaltAndCatchFireFromThread().Forget();
				return;
			}
			base.IsBurning = true;
			this.OnFireStart();
			ICircuitHolder circuitHousing = this.CircuitHousing;
			if (circuitHousing != null)
			{
				circuitHousing.HaltAndCatchFire();
			}
			Achievements.AchieveHaltAndCatchFire();
		}

		// Token: 0x06006F3A RID: 28474 RVA: 0x00216DE7 File Offset: 0x00214FE7
		public void Reset()
		{
			this._NextAddr = 0;
			this._Registers[this._StackPointerIndex] = 0.0;
			this._ErrorType = ProgrammableChipException.ICExceptionType.None;
			this._ErrorLineNumber = 0;
		}

		// Token: 0x06006F3B RID: 28475 RVA: 0x00216E14 File Offset: 0x00215014
		public override PassiveTooltip GetPassiveTooltip(Collider hitCollider)
		{
			PassiveTooltip passiveTooltip = base.GetPassiveTooltip(hitCollider);
			passiveTooltip.Extended += this.GetErrorCode();
			return passiveTooltip;
		}

		// Token: 0x06006F3C RID: 28476 RVA: 0x00216E3F File Offset: 0x0021503F
		public override StringBuilder GetExtendedText()
		{
			StringBuilder extendedText = base.GetExtendedText();
			extendedText.AppendLine(GameStrings.ItemInSlotValue.AsString(base.ToTooltip(), this.GetQuantityText()));
			return extendedText;
		}

		// Token: 0x06006F3D RID: 28477 RVA: 0x00216E64 File Offset: 0x00215064
		public string GetErrorCode()
		{
			if (this.CompilationError)
			{
				return GameStrings.ProgrammableChipErrorCode.AsString(ProgrammableChip._exceptionTypes.GetName(this.CompileErrorType, false), StringManager.Get((int)this.CompileErrorLineNumber)) + "\n";
			}
			if (this._ErrorType != ProgrammableChipException.ICExceptionType.None)
			{
				return GameStrings.ProgrammableChipErrorCode.AsString(ProgrammableChip._exceptionTypes.GetName(this._ErrorType, false), StringManager.Get((int)this._ErrorLineNumberSynced)) + "\n";
			}
			return string.Empty;
		}

		// Token: 0x06006F3E RID: 28478 RVA: 0x00216EE8 File Offset: 0x002150E8
		public void AppendErrorsToActionInstance(Thing.DelayedActionInstance actionInstance)
		{
			if (this.CompilationError)
			{
				actionInstance.AppendStateMessage(GameStrings.ProgrammableChipErrorCode, ProgrammableChip._exceptionTypes.GetName(this.CompileErrorType, false), StringManager.Get((int)this.CompileErrorLineNumber));
				return;
			}
			if (this._ErrorType != ProgrammableChipException.ICExceptionType.None)
			{
				actionInstance.AppendStateMessage(GameStrings.ProgrammableChipErrorCode, this.ErrorTypeString, this.ErrorLineNumberString);
			}
		}

		// Token: 0x06006F3F RID: 28479 RVA: 0x00216F44 File Offset: 0x00215144
		public static double LongToDouble(long l)
		{
			bool flag = (l & 9007199254740992L) != 0L;
			l &= 9007199254740991L;
			if (flag)
			{
				l |= -9007199254740992L;
			}
			return (double)l;
		}

		// Token: 0x06006F40 RID: 28480 RVA: 0x00216F74 File Offset: 0x00215174
		public static long DoubleToLong(double d, bool signed)
		{
			long num = (long)(d % 9007199254740992.0);
			if (!signed)
			{
				num &= 18014398509481983L;
			}
			return num;
		}

		// Token: 0x1700138B RID: 5003
		// (get) Token: 0x06006F41 RID: 28481 RVA: 0x00216F9E File Offset: 0x0021519E
		// (set) Token: 0x06006F42 RID: 28482 RVA: 0x00216FA8 File Offset: 0x002151A8
		public double LineNumber
		{
			get
			{
				return (double)this._NextAddr;
			}
			set
			{
				try
				{
					this._NextAddr = (int)Math.Clamp((long)((ulong)((uint)value)), 0L, (long)(this._LinesOfCode.Count - 1));
				}
				catch
				{
				}
			}
		}

		// Token: 0x06006F43 RID: 28483 RVA: 0x00216FEC File Offset: 0x002151EC
		public double ReadMemory(int address)
		{
			if (address < 0)
			{
				throw new StackUnderflowException();
			}
			if (address >= this._Stack.Length)
			{
				throw new StackOverflowException();
			}
			return this._Stack[address];
		}

		// Token: 0x06006F44 RID: 28484 RVA: 0x00217011 File Offset: 0x00215211
		public void WriteMemory(int address, double value)
		{
			if (address < 0)
			{
				throw new StackUnderflowException();
			}
			if (address >= this._Stack.Length)
			{
				throw new StackOverflowException();
			}
			this._Stack[address] = value;
		}

		// Token: 0x06006F45 RID: 28485 RVA: 0x00217038 File Offset: 0x00215238
		public void ClearMemory()
		{
			for (int i = 0; i < this._Stack.Length; i++)
			{
				this._Stack[i] = 0.0;
			}
		}

		// Token: 0x06006F46 RID: 28486 RVA: 0x00217069 File Offset: 0x00215269
		public int GetStackSize()
		{
			return this._Stack.Length;
		}

		// Token: 0x06006F47 RID: 28487 RVA: 0x00217073 File Offset: 0x00215273
		public static string StripColorTags(string input)
		{
			return Regex.Replace(input, "<color\\s*=\\s*['\"]?(?<color>[^'\">]+)['\"]?\\s*>(?<content>.*?)<\\/color>", "${content}");
		}

		// Token: 0x06006F48 RID: 28488 RVA: 0x00217088 File Offset: 0x00215288
		public static string GetCommandDescription(ScriptCommand command)
		{
			switch (command)
			{
			case ScriptCommand.l:
				return Localization.GetInterface("ScriptCommandL", false);
			case ScriptCommand.s:
				return Localization.GetInterface("ScriptCommandS", false);
			case ScriptCommand.ls:
				return Localization.GetInterface("ScriptCommandLS", false);
			case ScriptCommand.lr:
				return Localization.GetInterface("ScriptCommandLR", false);
			case ScriptCommand.sb:
				return Localization.GetInterface("ScriptCommandSB", false);
			case ScriptCommand.lb:
				return Localization.GetInterface("ScriptCommandLB", false);
			case ScriptCommand.alias:
				return Localization.GetInterface("ScriptCommandAlias", false);
			case ScriptCommand.move:
				return Localization.GetInterface("ScriptCommandMove", false);
			case ScriptCommand.add:
				return Localization.GetInterface("ScriptCommandAdd", false);
			case ScriptCommand.sub:
				return Localization.GetInterface("ScriptCommandSub", false);
			case ScriptCommand.sdse:
				return Localization.GetInterface("ScriptCommandSdse", false);
			case ScriptCommand.sdns:
				return Localization.GetInterface("ScriptCommandSdns", false);
			case ScriptCommand.slt:
				return Localization.GetInterface("ScriptCommandSlt", false);
			case ScriptCommand.sgt:
				return Localization.GetInterface("ScriptCommandSgt", false);
			case ScriptCommand.sle:
				return Localization.GetInterface("ScriptCommandSle", false);
			case ScriptCommand.sge:
				return Localization.GetInterface("ScriptCommandSge", false);
			case ScriptCommand.seq:
				return Localization.GetInterface("ScriptCommandSeq", false);
			case ScriptCommand.sne:
				return Localization.GetInterface("ScriptCommandSne", false);
			case ScriptCommand.sap:
				return Localization.GetInterface("ScriptCommandSap", false);
			case ScriptCommand.sna:
				return Localization.GetInterface("ScriptCommandSna", false);
			case ScriptCommand.and:
				return GameStrings.ScriptDescriptionAnd.DisplayString;
			case ScriptCommand.or:
				return GameStrings.ScriptDescriptionOr.DisplayString;
			case ScriptCommand.xor:
				return GameStrings.ScriptDescriptionXor.DisplayString;
			case ScriptCommand.nor:
				return GameStrings.ScriptDescriptionNor.DisplayString;
			case ScriptCommand.mul:
				return Localization.GetInterface("ScriptCommandMul", false);
			case ScriptCommand.div:
				return Localization.GetInterface("ScriptCommandDiv", false);
			case ScriptCommand.mod:
				return Localization.GetInterface("ScriptCommandMod", false);
			case ScriptCommand.j:
				return Localization.GetInterface("ScriptCommandJ", false);
			case ScriptCommand.bltz:
				return Localization.GetInterface("ScriptCommandBltz", false);
			case ScriptCommand.bgez:
				return Localization.GetInterface("ScriptCommandBgez", false);
			case ScriptCommand.blez:
				return Localization.GetInterface("ScriptCommandBlez", false);
			case ScriptCommand.bgtz:
				return Localization.GetInterface("ScriptCommandBgtz", false);
			case ScriptCommand.bdse:
				return Localization.GetInterface("ScriptCommandBdse", false);
			case ScriptCommand.bdns:
				return Localization.GetInterface("ScriptCommandBdns", false);
			case ScriptCommand.beq:
				return Localization.GetInterface("ScriptCommandBeq", false);
			case ScriptCommand.bne:
				return Localization.GetInterface("ScriptCommandBne", false);
			case ScriptCommand.bap:
				return Localization.GetInterface("ScriptCommandBap", false);
			case ScriptCommand.bna:
				return Localization.GetInterface("ScriptCommandBna", false);
			case ScriptCommand.jal:
				return Localization.GetInterface("ScriptCommandJal", false);
			case ScriptCommand.brdse:
				return Localization.GetInterface("ScriptCommandBrdse", false);
			case ScriptCommand.brdns:
				return Localization.GetInterface("ScriptCommandBrdns", false);
			case ScriptCommand.bltzal:
				return Localization.GetInterface("ScriptCommandBltzal", false);
			case ScriptCommand.bgezal:
				return Localization.GetInterface("ScriptCommandBgezal", false);
			case ScriptCommand.blezal:
				return Localization.GetInterface("ScriptCommandBlezal", false);
			case ScriptCommand.bgtzal:
				return Localization.GetInterface("ScriptCommandBgtzal", false);
			case ScriptCommand.beqal:
				return Localization.GetInterface("ScriptCommandBeqal", false);
			case ScriptCommand.bneal:
				return Localization.GetInterface("ScriptCommandBneal", false);
			case ScriptCommand.jr:
				return Localization.GetInterface("ScriptCommandJr", false);
			case ScriptCommand.bdseal:
				return Localization.GetInterface("ScriptCommandBdseal", false);
			case ScriptCommand.bdnsal:
				return Localization.GetInterface("ScriptCommandBdnsal", false);
			case ScriptCommand.brltz:
				return Localization.GetInterface("ScriptCommandBrltz", false);
			case ScriptCommand.brgez:
				return Localization.GetInterface("ScriptCommandBrgez", false);
			case ScriptCommand.brlez:
				return Localization.GetInterface("ScriptCommandBrlez", false);
			case ScriptCommand.brgtz:
				return Localization.GetInterface("ScriptCommandBrgtz", false);
			case ScriptCommand.breq:
				return Localization.GetInterface("ScriptCommandBreq", false);
			case ScriptCommand.brne:
				return Localization.GetInterface("ScriptCommandBrne", false);
			case ScriptCommand.brap:
				return Localization.GetInterface("ScriptCommandBrap", false);
			case ScriptCommand.brna:
				return Localization.GetInterface("ScriptCommandBrna", false);
			case ScriptCommand.sqrt:
				return Localization.GetInterface("ScriptCommandSqrt", false);
			case ScriptCommand.round:
				return Localization.GetInterface("ScriptCommandRound", false);
			case ScriptCommand.trunc:
				return Localization.GetInterface("ScriptCommandTrunc", false);
			case ScriptCommand.ceil:
				return Localization.GetInterface("ScriptCommandCeil", false);
			case ScriptCommand.floor:
				return Localization.GetInterface("ScriptCommandFloor", false);
			case ScriptCommand.max:
				return Localization.GetInterface("ScriptCommandMax", false);
			case ScriptCommand.min:
				return Localization.GetInterface("ScriptCommandMin", false);
			case ScriptCommand.abs:
				return Localization.GetInterface("ScriptCommandAbs", false);
			case ScriptCommand.log:
				return Localization.GetInterface("ScriptCommandLog", false);
			case ScriptCommand.exp:
				return Localization.GetInterface("ScriptCommandExp", false);
			case ScriptCommand.rand:
				return Localization.GetInterface("ScriptCommandRand", false);
			case ScriptCommand.yield:
				return Localization.GetInterface("ScriptCommandYield", false);
			case ScriptCommand.label:
				return Localization.GetInterface("ScriptCommandLabel", false);
			case ScriptCommand.peek:
				return Localization.GetInterface("ScriptCommandPeek", false);
			case ScriptCommand.push:
				return Localization.GetInterface("ScriptCommandPush", false);
			case ScriptCommand.pop:
				return Localization.GetInterface("ScriptCommandPop", false);
			case ScriptCommand.hcf:
				return Localization.GetInterface("ScriptCommandHcf", false);
			case ScriptCommand.select:
				return Localization.GetInterface("ScriptCommandSelect", false);
			case ScriptCommand.blt:
				return Localization.GetInterface("ScriptCommandBlt", false);
			case ScriptCommand.bgt:
				return Localization.GetInterface("ScriptCommandBgt", false);
			case ScriptCommand.ble:
				return Localization.GetInterface("ScriptCommandBle", false);
			case ScriptCommand.bge:
				return Localization.GetInterface("ScriptCommandBge", false);
			case ScriptCommand.brlt:
				return Localization.GetInterface("ScriptCommandBrlt", false);
			case ScriptCommand.brgt:
				return Localization.GetInterface("ScriptCommandBrgt", false);
			case ScriptCommand.brle:
				return Localization.GetInterface("ScriptCommandBrle", false);
			case ScriptCommand.brge:
				return Localization.GetInterface("ScriptCommandBrge", false);
			case ScriptCommand.bltal:
				return Localization.GetInterface("ScriptCommandBltal", false);
			case ScriptCommand.bgtal:
				return Localization.GetInterface("ScriptCommandBgtal", false);
			case ScriptCommand.bleal:
				return Localization.GetInterface("ScriptCommandBleal", false);
			case ScriptCommand.bgeal:
				return Localization.GetInterface("ScriptCommandBgeal", false);
			case ScriptCommand.bapal:
				return Localization.GetInterface("ScriptCommandBneal", false);
			case ScriptCommand.bnaal:
				return Localization.GetInterface("ScriptCommandBnaal", false);
			case ScriptCommand.beqz:
				return Localization.GetInterface("ScriptCommandBeqz", false);
			case ScriptCommand.bnez:
				return Localization.GetInterface("ScriptCommandBnez", false);
			case ScriptCommand.bapz:
				return Localization.GetInterface("ScriptCommandBapz", false);
			case ScriptCommand.bnaz:
				return Localization.GetInterface("ScriptCommandBnaz", false);
			case ScriptCommand.breqz:
				return Localization.GetInterface("ScriptCommandBreqz", false);
			case ScriptCommand.brnez:
				return Localization.GetInterface("ScriptCommandBrnez", false);
			case ScriptCommand.brapz:
				return Localization.GetInterface("ScriptCommandBrapz", false);
			case ScriptCommand.brnaz:
				return Localization.GetInterface("ScriptCommandBrnaz", false);
			case ScriptCommand.beqzal:
				return Localization.GetInterface("ScriptCommandBeqzal", false);
			case ScriptCommand.bnezal:
				return Localization.GetInterface("ScriptCommandBnezal", false);
			case ScriptCommand.bapzal:
				return Localization.GetInterface("ScriptCommandBapzal", false);
			case ScriptCommand.bnazal:
				return Localization.GetInterface("ScriptCommandBnazal", false);
			case ScriptCommand.sltz:
				return Localization.GetInterface("ScriptCommandSltz", false);
			case ScriptCommand.sgtz:
				return Localization.GetInterface("ScriptCommandSgtz", false);
			case ScriptCommand.slez:
				return Localization.GetInterface("ScriptCommandSlez", false);
			case ScriptCommand.sgez:
				return Localization.GetInterface("ScriptCommandSgez", false);
			case ScriptCommand.seqz:
				return Localization.GetInterface("ScriptCommandSeqz", false);
			case ScriptCommand.snez:
				return Localization.GetInterface("ScriptCommandSnez", false);
			case ScriptCommand.sapz:
				return Localization.GetInterface("ScriptCommandSapz", false);
			case ScriptCommand.snaz:
				return Localization.GetInterface("ScriptCommandSnaz", false);
			case ScriptCommand.define:
				return Localization.GetInterface("ScriptCommandDefine", false);
			case ScriptCommand.sleep:
				return Localization.GetInterface("ScriptCommandSleep", false);
			case ScriptCommand.sin:
				return Localization.GetInterface("ScriptCommandSin", false);
			case ScriptCommand.asin:
				return Localization.GetInterface("ScriptCommandASin", false);
			case ScriptCommand.tan:
				return Localization.GetInterface("ScriptCommandTan", false);
			case ScriptCommand.atan:
				return Localization.GetInterface("ScriptCommandATan", false);
			case ScriptCommand.cos:
				return Localization.GetInterface("ScriptCommandCos", false);
			case ScriptCommand.acos:
				return Localization.GetInterface("ScriptCommandCos", false);
			case ScriptCommand.atan2:
				return Localization.GetInterface("ScriptCommandATan2", false);
			case ScriptCommand.brnan:
				return Localization.GetInterface("ScriptCommandBrnan", false);
			case ScriptCommand.bnan:
				return Localization.GetInterface("ScriptCommandBnan", false);
			case ScriptCommand.snan:
				return Localization.GetInterface("ScriptCommandSnan", false);
			case ScriptCommand.snanz:
				return Localization.GetInterface("ScriptCommandSnanz", false);
			case ScriptCommand.lbs:
				return Localization.GetInterface("ScriptCommandLBS", false);
			case ScriptCommand.lbn:
				return Localization.GetInterface("ScriptCommandLBN", false);
			case ScriptCommand.sbn:
				return GameStrings.ScriptDescriptionSbn.DisplayString;
			case ScriptCommand.lbns:
				return Localization.GetInterface("ScriptCommandLBNS", false);
			case ScriptCommand.ss:
				return Localization.GetInterface("ScriptCommandSS", false);
			case ScriptCommand.sbs:
				return Localization.GetInterface("ScriptCommandSBS", false);
			case ScriptCommand.srl:
				return GameStrings.ScriptDescriptionSrl.DisplayString;
			case ScriptCommand.sra:
				return GameStrings.ScriptDescriptionSra.DisplayString;
			case ScriptCommand.sll:
				return GameStrings.ScriptDescriptionSll.DisplayString;
			case ScriptCommand.sla:
				return GameStrings.ScriptDescriptionSla.DisplayString;
			case ScriptCommand.not:
				return GameStrings.ScriptDescriptionNot.DisplayString;
			case ScriptCommand.ld:
				return Localization.GetInterface("ScriptCommandLD", false);
			case ScriptCommand.sd:
				return Localization.GetInterface("ScriptCommandSD", false);
			case ScriptCommand.poke:
				return GameStrings.ScriptDescriptionPoke.DisplayString;
			case ScriptCommand.getd:
				return GameStrings.ScriptDescriptionGetD.DisplayString;
			case ScriptCommand.putd:
				return GameStrings.ScriptDescriptionPutD.DisplayString;
			case ScriptCommand.get:
				return GameStrings.ScriptDescriptionGet.DisplayString;
			case ScriptCommand.put:
				return GameStrings.ScriptDescriptionPut.DisplayString;
			case ScriptCommand.clr:
				return GameStrings.ScriptDescriptionClr.DisplayString;
			case ScriptCommand.clrd:
				return GameStrings.ScriptDescriptionClrD.DisplayString;
			case ScriptCommand.rmap:
				return GameStrings.ScriptDescriptionRMap.DisplayString;
			case ScriptCommand.bdnvl:
				return GameStrings.ScriptDescriptionBdnvl.DisplayString;
			case ScriptCommand.bdnvs:
				return GameStrings.ScriptDescriptionBdnvs.DisplayString;
			case ScriptCommand.pow:
				return GameStrings.ScriptDescriptionPow.DisplayString;
			case ScriptCommand.ext:
				return GameStrings.ScriptDescriptionExt.DisplayString;
			case ScriptCommand.ins:
				return GameStrings.ScriptDescriptionIns.DisplayString;
			case ScriptCommand.lerp:
				return GameStrings.ScriptDescriptionLerp.DisplayString;
			default:
				throw new ArgumentOutOfRangeException(Localization.GetInterface("ScriptCommandCommand", false), command, null);
			}
		}

		// Token: 0x04005568 RID: 21864
		[SerializeField]
		[ReadOnly]
		private readonly double[] _Registers = new double[18];

		// Token: 0x04005569 RID: 21865
		private readonly int _StackPointerIndex = 16;

		// Token: 0x0400556A RID: 21866
		private readonly int _ReturnAddressIndex = 17;

		// Token: 0x0400556B RID: 21867
		private readonly double[] _Stack = new double[512];

		// Token: 0x0400556C RID: 21868
		public AsciiString SourceCode = AsciiString.Empty;

		// Token: 0x0400556F RID: 21871
		private readonly Dictionary<string, ProgrammableChip._AliasValue> _Aliases = new Dictionary<string, ProgrammableChip._AliasValue>();

		// Token: 0x04005570 RID: 21872
		private readonly Dictionary<string, int> _JumpTags = new Dictionary<string, int>();

		// Token: 0x04005571 RID: 21873
		private readonly Dictionary<string, double> _Defines = new Dictionary<string, double>();

		// Token: 0x04005572 RID: 21874
		[ByteArraySync]
		private ushort _ErrorLineNumberSynced;

		// Token: 0x04005574 RID: 21876
		private static EnumCollection<ProgrammableChipException.ICExceptionType, byte> _exceptionTypes = new EnumCollection<ProgrammableChipException.ICExceptionType, byte>(false);

		// Token: 0x04005575 RID: 21877
		[ByteArraySync]
		private byte _ErrorTypeSynced;

		// Token: 0x04005576 RID: 21878
		private ushort _compileErrorLineNumber;

		// Token: 0x04005577 RID: 21879
		private ProgrammableChipException.ICExceptionType _compileErrorType;

		// Token: 0x04005578 RID: 21880
		private int _NextAddr;

		// Token: 0x04005579 RID: 21881
		private readonly List<ProgrammableChip._LineOfCode> _LinesOfCode = new List<ProgrammableChip._LineOfCode>();

		// Token: 0x0400557A RID: 21882
		private int _executeIndex;

		// Token: 0x0400557B RID: 21883
		public const string _strCommand = "<color=yellow>{0}</color>";

		// Token: 0x0400557C RID: 21884
		public const string _strDevice = "<color=green>d?</color>";

		// Token: 0x0400557D RID: 21885
		public const string _strLogicType = "<color=orange>var</color>";

		// Token: 0x0400557E RID: 21886
		public const string _strNumber = "<color=white>num</color>";

		// Token: 0x0400557F RID: 21887
		public const string _strInteger = "<color=white>int</color>";

		// Token: 0x04005580 RID: 21888
		public const string _strOr = "<color=#585858FF>|</color>";

		// Token: 0x04005581 RID: 21889
		public const string _strRegister = "<color=#0080FFFF>r?</color>";

		// Token: 0x04005582 RID: 21890
		public const string _strString = "<color=white>str</color>";

		// Token: 0x04005583 RID: 21891
		public const string _strAny = "<color=#0080FFFF>r?</color>|<color=white>num</color>";

		// Token: 0x04005584 RID: 21892
		public const string _strRegOrDev = "<color=#0080FFFF>r?</color>|<color=green>d?</color>";

		// Token: 0x04005585 RID: 21893
		public const string _strReagentMode = "<color=orange>reagentMode</color>";

		// Token: 0x04005586 RID: 21894
		public const string _strReagent = "<color=white>reagent</color>";

		// Token: 0x04005587 RID: 21895
		public const string _strType = "<color=white>type</color>";

		// Token: 0x04005588 RID: 21896
		public const string _strBatchMode = "<color=orange>batchMode</color>";

		// Token: 0x04005589 RID: 21897
		public const string _strValues = "<color=lightblue>...</color>";

		// Token: 0x0400558A RID: 21898
		private const string FORMAT_VARIABLE = "<color=orange>{0}</color>";

		// Token: 0x0400558B RID: 21899
		private const string FORMAT_NUMBER = "<color=lightblue>{0}</color>";

		// Token: 0x0400558C RID: 21900
		private const string FORMAT_TEXT = "<color=white>{0}</color>";

		// Token: 0x0400558D RID: 21901
		private static readonly ProgrammableChip.HelpString STRING = new ProgrammableChip.HelpString("str", "white");

		// Token: 0x0400558E RID: 21902
		private static readonly ProgrammableChip.HelpString DEVICE_INDEX = new ProgrammableChip.HelpString("d?", "green");

		// Token: 0x0400558F RID: 21903
		private static readonly ProgrammableChip.HelpString REGISTER = new ProgrammableChip.HelpString("r?", "#0080FFFF");

		// Token: 0x04005590 RID: 21904
		private static readonly ProgrammableChip.HelpString INTEGER = new ProgrammableChip.HelpString("int", "#20B2AA");

		// Token: 0x04005591 RID: 21905
		private static readonly ProgrammableChip.HelpString NUMBER = new ProgrammableChip.HelpString("num", "#20B2AA");

		// Token: 0x04005592 RID: 21906
		private static readonly ProgrammableChip.HelpString REF_ID = new ProgrammableChip.HelpString("id", "#20B2AA");

		// Token: 0x04005593 RID: 21907
		private static readonly ProgrammableChip.HelpString OR = new ProgrammableChip.HelpString("or", "|", "#585858FF");

		// Token: 0x04005594 RID: 21908
		private static readonly ProgrammableChip.HelpString LOGIC_TYPE = new ProgrammableChip.HelpString("logicType", "orange");

		// Token: 0x04005595 RID: 21909
		private static readonly ProgrammableChip.HelpString LOGIC_SLOT_TYPE = new ProgrammableChip.HelpString("logicSlotType", "orange");

		// Token: 0x04005596 RID: 21910
		private static readonly ProgrammableChip.HelpString BATCH_MODE = new ProgrammableChip.HelpString("batchMode", "orange");

		// Token: 0x04005597 RID: 21911
		private static readonly ProgrammableChip.HelpString DEVICE_HASH = new ProgrammableChip.HelpString("deviceHash", "#20B2AA");

		// Token: 0x04005598 RID: 21912
		private static readonly ProgrammableChip.HelpString NAME_HASH = new ProgrammableChip.HelpString("nameHash", "#20B2AA");

		// Token: 0x04005599 RID: 21913
		private static readonly ProgrammableChip.HelpString SLOT_INDEX = new ProgrammableChip.HelpString("slotIndex", "#20B2AA");

		// Token: 0x0400559A RID: 21914
		private static readonly ProgrammableChip.HelpString REAGENT_MODE = new ProgrammableChip.HelpString("reagentMode", "orange");

		// Token: 0x0400559B RID: 21915
		public const char REGISTER_CHAR = 'r';

		// Token: 0x0400559C RID: 21916
		public const char DEVICE_CHAR = 'd';

		// Token: 0x0400559D RID: 21917
		public const string BASE_UNIT_STRING = "db";

		// Token: 0x0400559E RID: 21918
		public const int BASE_UNIT_INDEX = 2147483647;

		// Token: 0x0400559F RID: 21919
		public const int BASE_NETWORK_INDEX = -2147483648;

		// Token: 0x040055A0 RID: 21920
		public const int FIRST_AVAILABLE_NETWORK = 2147483647;

		// Token: 0x040055A1 RID: 21921
		public const string RETURN_ADDRESS_STRING = "ra";

		// Token: 0x040055A2 RID: 21922
		public const string STACK_POINTER_STRING = "sp";

		// Token: 0x040055A3 RID: 21923
		public const char HEX_CHAR = '$';

		// Token: 0x040055A4 RID: 21924
		public const char BINARY_CHAR = '%';

		// Token: 0x040055A5 RID: 21925
		public const char COMMENT_CHAR = '#';

		// Token: 0x040055A6 RID: 21926
		public const char NETWORK_CHAR = ':';

		// Token: 0x040055A7 RID: 21927
		public static ProgrammableChip.Constant[] AllConstants = new ProgrammableChip.Constant[]
		{
			new ProgrammableChip.Constant("nan", "A constant representing 'not a number'. This constant technically provides a 'quiet' NaN, a signal NaN from some instructions will result in an exception and halt execution", double.NaN, false),
			new ProgrammableChip.Constant("pinf", "A constant representing a positive infinite value", double.PositiveInfinity, false),
			new ProgrammableChip.Constant("ninf", "A constant representing a negative infinite value", double.NegativeInfinity, false),
			new ProgrammableChip.Constant("pi", "A constant representing ratio of the circumference of a circle to its diameter, provided in double precision", 3.141592653589793, true),
			new ProgrammableChip.Constant("tau", "A constant representing the ratio of the circumference of a circle to its radius, provided in double precision", 6.283185307179586, true),
			new ProgrammableChip.Constant("deg2rad", "Degrees to radians conversion constant", 0.01745329238474369, true),
			new ProgrammableChip.Constant("rad2deg", "Radians to degrees conversion constant", 57.295780181884766, true),
			new ProgrammableChip.Constant("epsilon", "A constant representing the smallest positive subnormal > 0", double.Epsilon, false),
			new ProgrammableChip.Constant("rgas", "Universal gas constant (J/(mol*K))", 8.31446261815324, true)
		};

		// Token: 0x040055A8 RID: 21928
		public static List<IScriptEnum> InternalEnums = new List<IScriptEnum>
		{
			new ProgrammableChip.ScriptEnum<LogicType>(InstructionInclude.LogicType, new Func<LogicType, bool>(LogicBase.IsDeprecated), new Func<LogicType, string>(LogicBase.GetLogicDescription)),
			new ProgrammableChip.ScriptEnum<LogicSlotType>(InstructionInclude.LogicSlotType, new Func<LogicSlotType, bool>(LogicBase.IsDeprecated), new Func<LogicSlotType, string>(LogicBase.GetLogicDescription)),
			new ProgrammableChip.ScriptEnum<LogicReagentMode>(InstructionInclude.LogicReagentMode, new Func<LogicReagentMode, bool>(LogicBase.IsDeprecated), null),
			new ProgrammableChip.ScriptEnum<LogicBatchMethod>(InstructionInclude.LogicBatchMethod, new Func<LogicBatchMethod, bool>(LogicBase.IsDeprecated), null),
			new ProgrammableChip.BasicEnum<LogicType>("LogicType", new Func<LogicType, bool>(LogicBase.IsDeprecated)),
			new ProgrammableChip.BasicEnum<LogicSlotType>("LogicSlotType", new Func<LogicSlotType, bool>(LogicBase.IsDeprecated)),
			new ProgrammableChip.BasicEnum<SoundAlert>("Sound", null),
			new ProgrammableChip.BasicEnum<LogicTransmitterMode>("TransmitterMode", null),
			new ProgrammableChip.BasicEnum<ElevatorMode>("ElevatorMode", null),
			new ProgrammableChip.BasicEnum<ColorType>("Color", null),
			new ProgrammableChip.BasicEnum<EntityState>("EntityState", null),
			new ProgrammableChip.BasicEnum<AirControlMode>("AirControl", null),
			new ProgrammableChip.BasicEnum<DaylightSensor.DaylightSensorMode>("DaylightSensorMode", null),
			new ProgrammableChip.BasicEnum<ConditionOperation>("", null),
			new ProgrammableChip.BasicEnum<AirConditioningMode>("AirCon", null),
			new ProgrammableChip.BasicEnum<VentDirection>("Vent", null),
			new ProgrammableChip.BasicEnum<PowerMode>("PowerMode", null),
			new ProgrammableChip.BasicEnum<RobotMode>("RobotMode", null),
			new ProgrammableChip.BasicEnum<SortingClass>("SortingClass", null),
			new ProgrammableChip.BasicEnum<Slot.Class>("SlotClass", null),
			new ProgrammableChip.BasicEnum<Chemistry.GasType>("GasType", null),
			new ProgrammableChip.BasicEnum<RocketMode>("RocketMode", null),
			new ProgrammableChip.BasicEnum<ReEntryProfile>("ReEntryProfile", null),
			new ProgrammableChip.BasicEnum<SorterInstruction>("SorterInstruction", null),
			new ProgrammableChip.BasicEnum<PrinterInstruction>("PrinterInstruction", null),
			new ProgrammableChip.BasicEnum<TraderInstruction>("TraderInstruction", null),
			new ProgrammableChip.BasicEnum<ShuttleType>("ShuttleType", null),
			new ProgrammableChip.BasicEnum<HashType>("HashType", null),
			new ProgrammableChip.BasicEnum<LogicDisplay.DisplayMode>("DisplayMode", null),
			new ProgrammableChip.BasicEnum<TraderContact.ContactTier>("ContactTier", null),
			new ProgrammableChip.BasicEnum<SettingDisplayMode>("SettingDisplayMode", null)
		};

		// Token: 0x020011D0 RID: 4560
		[Flags]
		private enum _AliasTarget
		{
			// Token: 0x0400724A RID: 29258
			None = 0,
			// Token: 0x0400724B RID: 29259
			Register = 1,
			// Token: 0x0400724C RID: 29260
			Device = 2,
			// Token: 0x0400724D RID: 29261
			Network = 4,
			// Token: 0x0400724E RID: 29262
			All = 268435455
		}

		// Token: 0x020011D1 RID: 4561
		private struct _AliasValue
		{
			// Token: 0x06008486 RID: 33926 RVA: 0x002922B2 File Offset: 0x002904B2
			public _AliasValue(ProgrammableChip._AliasTarget target, int index)
			{
				this.Target = target;
				this.Index = index;
			}

			// Token: 0x0400724F RID: 29263
			public readonly ProgrammableChip._AliasTarget Target;

			// Token: 0x04007250 RID: 29264
			public readonly int Index;
		}

		// Token: 0x020011D2 RID: 4562
		private struct HelpString
		{
			// Token: 0x06008487 RID: 33927 RVA: 0x002922C2 File Offset: 0x002904C2
			public HelpString(string str)
			{
				this._string = str;
			}

			// Token: 0x06008488 RID: 33928 RVA: 0x002922CB File Offset: 0x002904CB
			public HelpString(string type, string color)
			{
				this._string = string.Concat(new string[]
				{
					"<color=",
					color,
					">",
					type,
					"</color>"
				});
			}

			// Token: 0x06008489 RID: 33929 RVA: 0x002922FE File Offset: 0x002904FE
			public HelpString(string type, string output, string color)
			{
				this._string = string.Concat(new string[]
				{
					"<color=",
					color,
					">",
					output,
					"</color>"
				});
			}

			// Token: 0x0600848A RID: 33930 RVA: 0x00292331 File Offset: 0x00290531
			public HelpString(ProgrammableChip.HelpString parent, string format)
			{
				this._string = string.Format(format, parent._string);
			}

			// Token: 0x0600848B RID: 33931 RVA: 0x00292345 File Offset: 0x00290545
			public new readonly string ToString()
			{
				return this._string;
			}

			// Token: 0x0600848C RID: 33932 RVA: 0x0029234D File Offset: 0x0029054D
			public ProgrammableChip.HelpString Var(string variable)
			{
				return new ProgrammableChip.HelpString(variable + "(" + this._string + ")");
			}

			// Token: 0x0600848D RID: 33933 RVA: 0x0029236A File Offset: 0x0029056A
			public static ProgrammableChip.HelpString operator +(ProgrammableChip.HelpString left, ProgrammableChip.HelpString right)
			{
				return new ProgrammableChip.HelpString(left._string + ProgrammableChip.OR.ToString() + right._string);
			}

			// Token: 0x04007251 RID: 29265
			private readonly string _string;
		}

		// Token: 0x020011D3 RID: 4563
		public readonly struct Constant : IEquatable<ProgrammableChip.Constant>
		{
			// Token: 0x0600848E RID: 33934 RVA: 0x0029238C File Offset: 0x0029058C
			public bool Equals(ProgrammableChip.Constant other)
			{
				return this.Hash == other.Hash;
			}

			// Token: 0x0600848F RID: 33935 RVA: 0x0029239C File Offset: 0x0029059C
			public override bool Equals(object obj)
			{
				if (obj is ProgrammableChip.Constant)
				{
					ProgrammableChip.Constant other = (ProgrammableChip.Constant)obj;
					return this.Equals(other);
				}
				return false;
			}

			// Token: 0x06008490 RID: 33936 RVA: 0x002923C1 File Offset: 0x002905C1
			public override int GetHashCode()
			{
				return this.Hash;
			}

			// Token: 0x06008491 RID: 33937 RVA: 0x002923C9 File Offset: 0x002905C9
			public static bool operator ==(ProgrammableChip.Constant a, string b)
			{
				return !string.IsNullOrEmpty(b) && b.Equals(a.Literal, StringComparison.OrdinalIgnoreCase);
			}

			// Token: 0x06008492 RID: 33938 RVA: 0x002923E2 File Offset: 0x002905E2
			public static bool operator !=(ProgrammableChip.Constant a, string b)
			{
				return string.IsNullOrEmpty(b) || !b.Equals(a.Literal, StringComparison.OrdinalIgnoreCase);
			}

			// Token: 0x06008493 RID: 33939 RVA: 0x00292400 File Offset: 0x00290600
			public Constant(string literalString, string description, double value, bool addValueToDescription = true)
			{
				this.Literal = literalString;
				if (addValueToDescription)
				{
					this.Description = "<color=yellow>" + value.ToString("0." + new string('#', 339), CultureInfo.CurrentCulture) + "</color><br>" + description;
				}
				else
				{
					this.Description = description;
				}
				this.Value = value;
				this.Hash = Animator.StringToHash(this.Literal);
			}

			// Token: 0x06008494 RID: 33940 RVA: 0x00292471 File Offset: 0x00290671
			public string GetName()
			{
				return "<color=#20B2AA>" + this.Literal + "</color>";
			}

			// Token: 0x06008495 RID: 33941 RVA: 0x00292488 File Offset: 0x00290688
			public string GetValue()
			{
				if (RocketMath.Approximately(this.Value - Math.Floor(this.Value), 0.0, 0.01))
				{
					return this.Value.ToString("0", CultureInfo.CurrentCulture);
				}
				return this.Value.ToString("0." + new string('#', 8), CultureInfo.CurrentCulture);
			}

			// Token: 0x04007252 RID: 29266
			public readonly string Literal;

			// Token: 0x04007253 RID: 29267
			public readonly string Description;

			// Token: 0x04007254 RID: 29268
			public readonly double Value;

			// Token: 0x04007255 RID: 29269
			public readonly int Hash;
		}

		// Token: 0x020011D4 RID: 4564
		private class _LineOfCode
		{
			// Token: 0x06008496 RID: 33942 RVA: 0x00292500 File Offset: 0x00290700
			public _LineOfCode(ProgrammableChip chip, string lineOfCode, int lineNumber)
			{
				string text;
				if (lineOfCode.IndexOf('#') >= 0)
				{
					text = lineOfCode.Substring(0, lineOfCode.IndexOf('#'));
				}
				else
				{
					text = lineOfCode;
				}
				Localization.RegexResult matchesForStringPreprocessing = Localization.GetMatchesForStringPreprocessing(ref text);
				for (int i = 0; i < matchesForStringPreprocessing.Count(); i++)
				{
					text = text.Replace(matchesForStringPreprocessing.GetFull(i), ProgrammableChip.PackAscii6(matchesForStringPreprocessing.GetName(i), lineNumber).ToString(CultureInfo.InvariantCulture));
				}
				try
				{
					Localization.RegexResult matchesForHashPreprocessing = Localization.GetMatchesForHashPreprocessing(ref text);
					for (int j = 0; j < matchesForHashPreprocessing.Count(); j++)
					{
						text = text.Replace(matchesForHashPreprocessing.GetFull(j), Animator.StringToHash(matchesForHashPreprocessing.GetName(j)).ToString());
					}
				}
				catch (Exception)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidPreprocessHash, lineNumber);
				}
				try
				{
					Localization.RegexResult matchesForBinaryPreprocessing = Localization.GetMatchesForBinaryPreprocessing(ref text);
					for (int k = 0; k < matchesForBinaryPreprocessing.Count(); k++)
					{
						string value = matchesForBinaryPreprocessing.GetName(k).Replace("_", "");
						text = text.Replace(matchesForBinaryPreprocessing.GetFull(k), Convert.ToInt64(value, 2).ToString());
					}
				}
				catch (Exception)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidProcessBinary, lineNumber);
				}
				try
				{
					Localization.RegexResult matchesForHexPreprocessing = Localization.GetMatchesForHexPreprocessing(ref text);
					for (int l = 0; l < matchesForHexPreprocessing.Count(); l++)
					{
						string value2 = matchesForHexPreprocessing.GetName(l).Replace("_", "");
						text = text.Replace(matchesForHexPreprocessing.GetFull(l), Convert.ToInt64(value2, 16).ToString());
					}
				}
				catch (Exception)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidPreprocessHex, lineNumber);
				}
				string[] array = text.Split(Array.Empty<char>());
				for (int m = array.Length - 1; m >= 0; m--)
				{
					if (string.IsNullOrEmpty(array[m]))
					{
						array = array.RemoveAt(m);
					}
				}
				if (array.Length == 0)
				{
					this.Operation = new ProgrammableChip._NOOP_Operation(chip, lineNumber);
				}
				else if (array.Length == 1 && array[0].Length >= 2 && array[0][array[0].Length - 1] == ':')
				{
					this.Operation = new ProgrammableChip._NOOP_Operation(chip, lineNumber);
					string key = array[0].Substring(0, array[0].Length - 1);
					if (chip._JumpTags.ContainsKey(key))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.JumpTagDuplicate, lineNumber);
					}
					chip._JumpTags.Add(key, lineNumber);
				}
				else
				{
					if (!Enum.IsDefined(typeof(ScriptCommand), array[0]))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.UnrecognisedInstruction, lineNumber);
					}
					switch ((ScriptCommand)Enum.Parse(typeof(ScriptCommand), array[0]))
					{
					case ScriptCommand.l:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._L_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.s:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._S_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.ls:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.lr:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LR_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.sb:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SB_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.lb:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LB_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.alias:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ALIAS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.move:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._MOVE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.add:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ADD_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sub:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SUB_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sdse:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SDSE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.sdns:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SDNS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.slt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sgt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SGT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sle:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sge:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SGE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.seq:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SEQ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sne:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sap:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SAP_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.sna:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNA_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.and:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._AND_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.or:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._OR_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.xor:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._XOR_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.nor:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._NOR_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.mul:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._MUL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.div:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._DIV_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.mod:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._MOD_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.j:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._J_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.bltz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bgez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.blez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bgtz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bdse:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDSE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bdns:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDNS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.beq:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BEQ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bne:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bap:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BAP_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.bna:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNA_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.jal:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._JAL_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.brdse:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRDSE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brdns:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRDNS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bltzal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLTZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bgezal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGEZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.blezal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLEZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bgtzal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGTZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.beqal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BEQAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bneal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNEAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.jr:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._JR_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.bdseal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDSEAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bdnsal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDNSAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brltz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRLTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brgez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRGEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brlez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRLEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brgtz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRGTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.breq:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BREQ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brne:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRNE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brap:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRAP_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.brna:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRNA_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.sqrt:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SQRT_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.round:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ROUND_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.trunc:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._TRUNC_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.ceil:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._CEIL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.floor:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._FLOOR_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.max:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._MAX_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.min:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._MIN_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.abs:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ABS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.log:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LOG_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.exp:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._EXP_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.rand:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._RAND_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.yield:
						if (array.Length != 1)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._YIELD_Operation(chip, lineNumber);
						break;
					case ScriptCommand.label:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LABEL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.peek:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._PEEK_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.push:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._PUSH_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.pop:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._POP_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.hcf:
						if (array.Length != 1)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._HCF_Operation(chip, lineNumber);
						break;
					case ScriptCommand.select:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SELECT_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.blt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bgt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.ble:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bge:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brlt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRLT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brgt:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRGT_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brle:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRLE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brge:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRGE_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bltal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLTAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bgtal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGTAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bleal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BLEAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bgeal:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BGEAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bapal:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BAPAL_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.bnaal:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNAAL_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.beqz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BEQZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bnez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bapz:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BAPZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bnaz:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNAZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.breqz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BREQZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brnez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRNEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.brapz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRAPZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brnaz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRNAZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.beqzal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BEQZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bnezal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNEZAL_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bapzal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BAPZAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bnazal:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNAZAL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sltz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.sgtz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SGTZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.slez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.sgez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SGEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.seqz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SEQZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.snez:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNEZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.sapz:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SAPZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.snaz:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNAZ_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.define:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._DEFINE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.sleep:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLEEP_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.sin:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SIN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.asin:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ASIN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.tan:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._TAN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.atan:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ATAN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.cos:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._COS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.acos:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ACOS_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.atan2:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ATAN2_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.brnan:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BRNAN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.bnan:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BNAN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.snan:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNAN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.snanz:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SNANZ_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.lbs:
						if (array.Length != 6)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LBS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4], array[5]);
						break;
					case ScriptCommand.lbn:
						if (array.Length != 6)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LBN_Operation(chip, lineNumber, array[1], array[2], array[3], array[4], array[5]);
						break;
					case ScriptCommand.sbn:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SBN_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.lbns:
						if (array.Length != 7)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LBNS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4], array[5], array[6]);
						break;
					case ScriptCommand.ss:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.sbs:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SBS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.srl:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SRL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sra:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SRA_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sll:
					case ScriptCommand.sla:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SLA_SLL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.not:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._NOT_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.ld:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LD_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.sd:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SD_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.poke:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._POKE_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.getd:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._GETD_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.putd:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._PUTD_Operation(chip, lineNumber, array[3], array[1], array[2]);
						break;
					case ScriptCommand.get:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._GET_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.put:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._PUT_Operation(chip, lineNumber, array[3], array[1], array[2]);
						break;
					case ScriptCommand.clr:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._CLR_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.clrd:
						if (array.Length != 2)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._CLRD_Operation(chip, lineNumber, array[1]);
						break;
					case ScriptCommand.rmap:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._RMAP_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bdnvl:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDNVL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.bdnvs:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._BDNVS_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.pow:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._POW_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.ext:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._EXT_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.ins:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._INS_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.lerp:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._LERP_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					default:
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.UnrecognisedInstruction, lineNumber);
					}
				}
				this.LineOfCode = lineOfCode;
			}

			// Token: 0x06008497 RID: 33943 RVA: 0x0029414C File Offset: 0x0029234C
			public override string ToString()
			{
				return this.LineOfCode;
			}

			// Token: 0x04007256 RID: 29270
			public readonly ProgrammableChip._Operation Operation;

			// Token: 0x04007257 RID: 29271
			public readonly string LineOfCode;
		}

		// Token: 0x020011D5 RID: 4565
		private abstract class _Operation
		{
			// Token: 0x06008498 RID: 33944 RVA: 0x00294154 File Offset: 0x00292354
			public _Operation(ProgrammableChip chip, int lineNumber)
			{
				this._Chip = chip;
				this._LineNumber = lineNumber;
			}

			// Token: 0x06008499 RID: 33945 RVA: 0x0029416A File Offset: 0x0029236A
			protected LogicType _GetLogicType(string logicTypeCode)
			{
				if (!Enum.IsDefined(typeof(LogicType), logicTypeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				return (LogicType)Enum.Parse(typeof(LogicType), logicTypeCode);
			}

			// Token: 0x0600849A RID: 33946 RVA: 0x002941A0 File Offset: 0x002923A0
			protected LogicSlotType _GetLogicSlotType(string logicTypeCode)
			{
				if (!Enum.IsDefined(typeof(LogicSlotType), logicTypeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				return (LogicSlotType)Enum.Parse(typeof(LogicSlotType), logicTypeCode);
			}

			// Token: 0x0600849B RID: 33947 RVA: 0x002941D6 File Offset: 0x002923D6
			protected LogicReagentMode _GetLogicReagentMode(string logicReagentModeCode)
			{
				if (!Enum.IsDefined(typeof(LogicReagentMode), logicReagentModeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentMode, this._LineNumber);
				}
				return (LogicReagentMode)Enum.Parse(typeof(LogicReagentMode), logicReagentModeCode);
			}

			// Token: 0x0600849C RID: 33948 RVA: 0x0029420D File Offset: 0x0029240D
			protected void _SetDeviceValue(Device device, LogicType logicType, double value)
			{
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotSet, this._LineNumber);
				}
				device.SetLogicValue(logicType, value);
			}

			// Token: 0x0600849D RID: 33949
			public abstract int Execute(int index);

			// Token: 0x0600849E RID: 33950 RVA: 0x00294230 File Offset: 0x00292430
			protected static ProgrammableChip._Operation.IDeviceVariable _MakeDeviceVariable(ProgrammableChip chip, int lineNumber, string deviceCode)
			{
				if (deviceCode.Length > 0 && (deviceCode[0] == '$' || deviceCode[0] == '%' || char.IsDigit(deviceCode[0])))
				{
					return new ProgrammableChip._Operation.DirectDeviceVariable(chip, lineNumber, deviceCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.DeviceIndex | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.NetworkIndex, false);
				}
				if (deviceCode.Length > 1 && deviceCode[0] == 'r' && char.IsDigit(deviceCode[1]))
				{
					return new ProgrammableChip._Operation.DirectDeviceVariable(chip, lineNumber, deviceCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.DeviceIndex | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.NetworkIndex, false);
				}
				string[] array = deviceCode.Split(':', StringSplitOptions.None);
				if (array.Length != 0 && array[0].StartsWith('d'))
				{
					string text = array[0];
					if (text == "db")
					{
						return new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
					}
					if (Regex.IsMatch(text, "^(d[0-9]|dr*[r0-9][0-9])$"))
					{
						return new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
					}
				}
				return new ProgrammableChip._Operation.DeviceAliasVariable(chip, lineNumber, deviceCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.DeviceIndex | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.NetworkIndex, false);
			}

			// Token: 0x04007258 RID: 29272
			protected readonly ProgrammableChip _Chip;

			// Token: 0x04007259 RID: 29273
			protected readonly int _LineNumber;

			// Token: 0x0400725A RID: 29274
			protected const string _ZeroString = "0";

			// Token: 0x020012E2 RID: 4834
			public class Variable
			{
				// Token: 0x0600868D RID: 34445 RVA: 0x0029D1EC File Offset: 0x0029B3EC
				protected Variable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true)
				{
					this._Chip = chip;
					this._LineNumber = lineNumber;
					this._PropertiesToUse = propertiesToUse;
					if ((propertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None)
					{
						this._RegisterIndex = this._GetRegisterIndex(code, out this._RegisterRecurse, throwException);
					}
					if ((propertiesToUse & (InstructionInclude.Alias | InstructionInclude.JumpTag)) != InstructionInclude.None)
					{
						if (code.Contains(':'))
						{
							code = code.Split(':', 2, StringSplitOptions.None)[0];
						}
						this._Alias = code;
					}
				}

				// Token: 0x0600868E RID: 34446 RVA: 0x0029D264 File Offset: 0x0029B464
				private int _GetIndex(string rCode, char firstLetter, bool throwException = true)
				{
					int num;
					return this._GetIndex(rCode, firstLetter, out num, throwException);
				}

				// Token: 0x0600868F RID: 34447 RVA: 0x0029D27C File Offset: 0x0029B47C
				private int _GetIndex(string rCode, char firstLetter, out int recurseCount, bool throwException = true)
				{
					int num = 0;
					while (rCode[num] == firstLetter)
					{
						num++;
					}
					if (num == 0)
					{
						if (throwException)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
						}
						recurseCount = -1;
						return -1;
					}
					else
					{
						string text = rCode.Substring(num);
						if (text.Contains(':'))
						{
							text = text.Split(':', 2, StringSplitOptions.None)[0];
						}
						int result;
						if (int.TryParse(text, out result))
						{
							recurseCount = num;
							return result;
						}
						if (throwException)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
						}
						recurseCount = -1;
						return -1;
					}
				}

				// Token: 0x06008690 RID: 34448 RVA: 0x0029D2F7 File Offset: 0x0029B4F7
				protected int _GetRegisterIndex(string rCode, out int recurseCount, bool throwException = true)
				{
					return this._GetIndex(rCode, 'r', out recurseCount, throwException);
				}

				// Token: 0x06008691 RID: 34449 RVA: 0x0029D304 File Offset: 0x0029B504
				protected int _GetDeviceIndex(string dCode, out int recurseCount, bool throwException = true)
				{
					if (dCode.StartsWith("db", StringComparison.Ordinal))
					{
						recurseCount = 0;
						return int.MaxValue;
					}
					if (dCode.Length >= 2 && dCode[1] == 'r')
					{
						return this._GetIndex(dCode.Substring(1), 'r', out recurseCount, throwException);
					}
					recurseCount = 0;
					return this._GetIndex(dCode, 'd', throwException);
				}

				// Token: 0x06008692 RID: 34450 RVA: 0x0029D35C File Offset: 0x0029B55C
				protected int _GetNetworkIndex(string dCode, bool throwException = true)
				{
					if (!dCode.Contains(':'))
					{
						return int.MinValue;
					}
					dCode = dCode.Split(':', 2, StringSplitOptions.None)[1];
					int result;
					if (int.TryParse(dCode, out result))
					{
						return result;
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return int.MinValue;
				}

				// Token: 0x06008693 RID: 34451 RVA: 0x0029D3A8 File Offset: 0x0029B5A8
				protected ProgrammableChip._AliasTarget GetAliasType(string alias, bool throwException = true)
				{
					ProgrammableChip._AliasValue aliasValue;
					if (!string.IsNullOrEmpty(this._Alias) && this._Chip._Aliases.TryGetValue(alias, out aliasValue))
					{
						return aliasValue.Target;
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return ProgrammableChip._AliasTarget.None;
				}

				// Token: 0x06008694 RID: 34452 RVA: 0x0029D3EF File Offset: 0x0029B5EF
				public bool TryParseAliasAsJumpTagValue(out int value, bool throwException = true)
				{
					if (this._Alias == null || !this._Chip._JumpTags.ContainsKey(this._Alias))
					{
						value = 0;
						return false;
					}
					value = this._Chip._JumpTags[this._Alias];
					return true;
				}

				// Token: 0x04007465 RID: 29797
				protected readonly ProgrammableChip _Chip;

				// Token: 0x04007466 RID: 29798
				protected readonly int _LineNumber;

				// Token: 0x04007467 RID: 29799
				protected readonly InstructionInclude _PropertiesToUse;

				// Token: 0x04007468 RID: 29800
				protected readonly int _RegisterIndex = -1;

				// Token: 0x04007469 RID: 29801
				protected readonly int _RegisterRecurse = -1;

				// Token: 0x0400746A RID: 29802
				protected readonly string _Alias;
			}

			// Token: 0x020012E3 RID: 4835
			protected class IndexVariable : ProgrammableChip._Operation.IntValuedVariable
			{
				// Token: 0x06008695 RID: 34453 RVA: 0x0029D42F File Offset: 0x0029B62F
				public IndexVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
				}

				// Token: 0x06008696 RID: 34454 RVA: 0x0029D440 File Offset: 0x0029B640
				protected bool TryParseAliasAsIndex(ProgrammableChip._AliasTarget type, out int index)
				{
					if (this._Alias == null || !this._Chip._Aliases.ContainsKey(this._Alias))
					{
						index = -1;
						return false;
					}
					ProgrammableChip._AliasValue aliasValue = this._Chip._Aliases[this._Alias];
					if (aliasValue.Target != type)
					{
						index = -1;
						return false;
					}
					index = aliasValue.Index;
					return true;
				}

				// Token: 0x06008697 RID: 34455 RVA: 0x0029D4A0 File Offset: 0x0029B6A0
				protected bool TryParseRegisterIndexAsIndex(out int index, bool throwException = true)
				{
					if (this._RegisterIndex < 0 || this._RegisterRecurse < 0)
					{
						index = -1;
						return false;
					}
					int num = this._RegisterIndex;
					int registerRecurse = this._RegisterRecurse;
					while (registerRecurse-- > 1)
					{
						num = (int)Math.Round(this._Chip._Registers[num]);
						if (num < 0 || num >= this._Chip._Registers.Length)
						{
							if (throwException)
							{
								throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfRegisterBounds, this._LineNumber);
							}
							index = -1;
							return false;
						}
					}
					index = num;
					return true;
				}

				// Token: 0x06008698 RID: 34456 RVA: 0x0029D520 File Offset: 0x0029B720
				public virtual int GetVariableIndex(ProgrammableChip._AliasTarget type, bool throwError = true)
				{
					int num = 0;
					double num2;
					int num3;
					if ((this._PropertiesToUse & InstructionInclude.Define) != InstructionInclude.None && this._Chip._Defines.TryGetValue(this._Alias, out num2))
					{
						num = (int)num2;
					}
					else if ((this._PropertiesToUse & InstructionInclude.Alias) != InstructionInclude.None && this.TryParseAliasAsIndex(type, out num3))
					{
						num = num3;
					}
					else if ((this._PropertiesToUse & InstructionInclude.JumpTag) != InstructionInclude.None && base.TryParseAliasAsJumpTagValue(out num3, true))
					{
						num = num3;
					}
					else if ((this._PropertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None && this.TryParseRegisterIndexAsIndex(out num3, throwError))
					{
						num = num3;
					}
					else if (throwError)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					if (throwError)
					{
						if ((type & ProgrammableChip._AliasTarget.Register) != ProgrammableChip._AliasTarget.None && (num < 0 || num >= this._Chip._Registers.Length))
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfRegisterBounds, this._LineNumber);
						}
						if ((type & ProgrammableChip._AliasTarget.Device) != ProgrammableChip._AliasTarget.None && !this._Chip.CircuitHousing.IsValidIndex(num))
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfDeviceBounds, this._LineNumber);
						}
					}
					return num;
				}
			}

			// Token: 0x020012E4 RID: 4836
			protected class ValueVariable : ProgrammableChip._Operation.Variable
			{
				// Token: 0x06008699 RID: 34457 RVA: 0x0029D605 File Offset: 0x0029B805
				public ValueVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
				}

				// Token: 0x0600869A RID: 34458 RVA: 0x0029D614 File Offset: 0x0029B814
				protected bool TryParseAliasAsValue(ProgrammableChip._AliasTarget type, out double value, bool throwException = true)
				{
					if (this._Alias == null || type != ProgrammableChip._AliasTarget.Register || !this._Chip._Aliases.ContainsKey(this._Alias))
					{
						value = double.NaN;
						return false;
					}
					ProgrammableChip._AliasValue aliasValue = this._Chip._Aliases[this._Alias];
					if (aliasValue.Target != type)
					{
						value = double.NaN;
						return false;
					}
					if (aliasValue.Index >= 0 && aliasValue.Index < this._Chip._Registers.Length)
					{
						value = this._Chip._Registers[aliasValue.Index];
						return true;
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfRegisterBounds, this._LineNumber);
					}
					value = double.NaN;
					return false;
				}

				// Token: 0x0600869B RID: 34459 RVA: 0x0029D6D0 File Offset: 0x0029B8D0
				protected bool TryParseRegisterIndexAsValue(out double value, bool throwException = true)
				{
					if (this._RegisterIndex < 0 || this._RegisterRecurse < 0)
					{
						value = double.NaN;
						return false;
					}
					int num = this._RegisterIndex;
					int registerRecurse = this._RegisterRecurse;
					while (registerRecurse-- > 1)
					{
						num = (int)Math.Round(this._Chip._Registers[num]);
						if (num < 0 || num >= this._Chip._Registers.Length)
						{
							if (throwException)
							{
								throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfRegisterBounds, this._LineNumber);
							}
							value = double.NaN;
							return false;
						}
					}
					value = this._Chip._Registers[num];
					return true;
				}
			}

			// Token: 0x020012E5 RID: 4837
			protected class DoubleValueVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x0600869C RID: 34460 RVA: 0x0029D76A File Offset: 0x0029B96A
				public double Get()
				{
					return this._Value;
				}

				// Token: 0x0600869D RID: 34461 RVA: 0x0029D774 File Offset: 0x0029B974
				public DoubleValueVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					bool flag = false;
					this._Value = double.NaN;
					this._qNaN = false;
					foreach (IScriptEnum scriptEnum in ProgrammableChip.InternalEnums)
					{
						scriptEnum.Execute(ref flag, ref this._Value, code, propertiesToUse);
					}
					if (!flag && (propertiesToUse & InstructionInclude.Value) != InstructionInclude.None)
					{
						foreach (ProgrammableChip.Constant constant in ProgrammableChip.AllConstants)
						{
							if (constant == code)
							{
								this._Value = constant.Value;
								return;
							}
						}
						double value;
						if (double.TryParse(code, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out value))
						{
							this._Value = value;
						}
					}
				}

				// Token: 0x0600869E RID: 34462 RVA: 0x0029D860 File Offset: 0x0029BA60
				protected bool TryParseValueAsValue(out double value)
				{
					if (!this._qNaN && double.IsNaN(this._Value))
					{
						value = double.NaN;
						return false;
					}
					value = this._Value;
					return true;
				}

				// Token: 0x0600869F RID: 34463 RVA: 0x0029D890 File Offset: 0x0029BA90
				public long GetVariableLong(ProgrammableChip._AliasTarget type, bool signed = true, bool errorAtEnd = true)
				{
					double variableValue = this.GetVariableValue(type, errorAtEnd);
					if (variableValue < -9.223372036854776E+18)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
					}
					if (variableValue <= 9.223372036854776E+18)
					{
						return ProgrammableChip.DoubleToLong(variableValue, signed);
					}
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftOverflow, this._LineNumber);
				}

				// Token: 0x060086A0 RID: 34464 RVA: 0x0029D8E4 File Offset: 0x0029BAE4
				public int GetVariableInt(ProgrammableChip._AliasTarget type, bool errorAtEnd = true)
				{
					double variableValue = this.GetVariableValue(type, errorAtEnd);
					if (variableValue < -2147483648.0)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
					}
					if (variableValue <= 2147483647.0)
					{
						return (int)variableValue;
					}
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftOverflow, this._LineNumber);
				}

				// Token: 0x060086A1 RID: 34465 RVA: 0x0029D934 File Offset: 0x0029BB34
				public double GetVariableValue(ProgrammableChip._AliasTarget type, bool errorAtEnd = true)
				{
					if ((type & ProgrammableChip._AliasTarget.Register) == ProgrammableChip._AliasTarget.None)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					double result;
					if ((this._PropertiesToUse & InstructionInclude.Define) != InstructionInclude.None && this._Chip._Defines.TryGetValue(this._Alias, out result))
					{
						return result;
					}
					double result2;
					if ((this._PropertiesToUse & InstructionInclude.Alias) != InstructionInclude.None && base.TryParseAliasAsValue(type, out result2, true))
					{
						return result2;
					}
					int num;
					if ((this._PropertiesToUse & InstructionInclude.JumpTag) != InstructionInclude.None && base.TryParseAliasAsJumpTagValue(out num, true))
					{
						return (double)num;
					}
					double result3;
					if ((this._PropertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None && base.TryParseRegisterIndexAsValue(out result3, errorAtEnd))
					{
						return result3;
					}
					double result4;
					if ((this._PropertiesToUse & (InstructionInclude.Value | InstructionInclude.Enum | InstructionInclude.LogicType | InstructionInclude.LogicSlotType | InstructionInclude.LogicReagentMode | InstructionInclude.LogicBatchMethod)) != InstructionInclude.None && this.TryParseValueAsValue(out result4))
					{
						return result4;
					}
					if (errorAtEnd)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return double.NaN;
				}

				// Token: 0x0400746B RID: 29803
				private readonly double _Value = double.NaN;

				// Token: 0x0400746C RID: 29804
				private readonly bool _qNaN;
			}

			// Token: 0x020012E6 RID: 4838
			protected class LineNumberVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x060086A2 RID: 34466 RVA: 0x0029D9F5 File Offset: 0x0029BBF5
				public LineNumberVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.Value) != InstructionInclude.None)
					{
						if (int.TryParse(code, out this._Value))
						{
							this._IsValueSet = true;
							return;
						}
						this._IsValueSet = false;
						this._Value = 0;
					}
				}

				// Token: 0x060086A3 RID: 34467 RVA: 0x0029DA30 File Offset: 0x0029BC30
				public int GetVariableValue(ProgrammableChip._AliasTarget type, bool throwException = true)
				{
					double num;
					if ((type & ProgrammableChip._AliasTarget.Register) != ProgrammableChip._AliasTarget.None && base.TryParseAliasAsValue(type, out num, throwException))
					{
						return (int)num;
					}
					double num2;
					if ((this._PropertiesToUse & InstructionInclude.Define) != InstructionInclude.None && this._Chip._Defines.TryGetValue(this._Alias, out num2))
					{
						return (int)num2;
					}
					int result;
					if ((this._PropertiesToUse & InstructionInclude.JumpTag) != InstructionInclude.None && base.TryParseAliasAsJumpTagValue(out result, true))
					{
						return result;
					}
					double a;
					if ((this._PropertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None && base.TryParseRegisterIndexAsValue(out a, throwException))
					{
						return (int)Math.Round(a);
					}
					if ((this._PropertiesToUse & (InstructionInclude.Value | InstructionInclude.Enum | InstructionInclude.LogicType | InstructionInclude.LogicSlotType | InstructionInclude.LogicReagentMode | InstructionInclude.LogicBatchMethod)) != InstructionInclude.None && this._IsValueSet)
					{
						return this._Value;
					}
					if ((this._PropertiesToUse & (InstructionInclude.Value | InstructionInclude.Enum | InstructionInclude.LogicType | InstructionInclude.LogicSlotType | InstructionInclude.LogicReagentMode | InstructionInclude.LogicBatchMethod)) != InstructionInclude.None && this._IsValueSet)
					{
						return this._Value;
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return -1;
				}

				// Token: 0x0400746D RID: 29805
				private readonly bool _IsValueSet;

				// Token: 0x0400746E RID: 29806
				private readonly int _Value;
			}

			// Token: 0x020012E7 RID: 4839
			protected interface IDeviceVariable
			{
				// Token: 0x060086A4 RID: 34468
				int GetNetworkIndex();

				// Token: 0x060086A5 RID: 34469
				ILogicable GetDevice(ICircuitHolder chipCircuitHousing);
			}

			// Token: 0x020012E8 RID: 4840
			protected class DirectDeviceVariable : ProgrammableChip._Operation.IntValuedVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x060086A6 RID: 34470 RVA: 0x0029DAF8 File Offset: 0x0029BCF8
				public DirectDeviceVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						this._DeviceNetwork = base._GetNetworkIndex(code, throwException);
					}
				}

				// Token: 0x060086A7 RID: 34471 RVA: 0x0029DB2B File Offset: 0x0029BD2B
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x060086A8 RID: 34472 RVA: 0x0029DB34 File Offset: 0x0029BD34
				public ILogicable GetDevice(ICircuitHolder chipCircuitHousing)
				{
					int variableValue = base.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
					return chipCircuitHousing.GetLogicableFromId(variableValue, this._DeviceNetwork);
				}

				// Token: 0x0400746F RID: 29807
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x020012E9 RID: 4841
			protected class DeviceAliasVariable : ProgrammableChip._Operation.IndexVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x060086A9 RID: 34473 RVA: 0x0029DB57 File Offset: 0x0029BD57
				public DeviceAliasVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						this._DeviceNetwork = base._GetNetworkIndex(code, throwException);
					}
				}

				// Token: 0x060086AA RID: 34474 RVA: 0x0029DB8A File Offset: 0x0029BD8A
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x060086AB RID: 34475 RVA: 0x0029DB94 File Offset: 0x0029BD94
				public ILogicable GetDevice(ICircuitHolder chipCircuitHousing)
				{
					if (string.IsNullOrEmpty(this._Alias))
					{
						int variableIndex = this.GetVariableIndex(ProgrammableChip._AliasTarget.Device, false);
						return chipCircuitHousing.GetLogicableFromIndex(variableIndex, this._DeviceNetwork);
					}
					ProgrammableChip._AliasTarget aliasType = base.GetAliasType(this._Alias, true);
					if (aliasType == ProgrammableChip._AliasTarget.Device)
					{
						int variableIndex2 = this.GetVariableIndex(aliasType, false);
						return chipCircuitHousing.GetLogicableFromIndex(variableIndex2, this._DeviceNetwork);
					}
					if (aliasType == ProgrammableChip._AliasTarget.Register)
					{
						int variableValue = base.GetVariableValue(aliasType, false);
						return chipCircuitHousing.GetLogicableFromId(variableValue, this._DeviceNetwork);
					}
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.AliasNotFound, this._LineNumber);
				}

				// Token: 0x04007470 RID: 29808
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x020012EA RID: 4842
			protected class DeviceIndexVariable : ProgrammableChip._Operation.IndexVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x060086AC RID: 34476 RVA: 0x0029DC18 File Offset: 0x0029BE18
				public DeviceIndexVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.DeviceIndex) != InstructionInclude.None)
					{
						this._DeviceIndex = base._GetDeviceIndex(code, out this._DeviceRecurse, throwException);
					}
					if ((propertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						this._DeviceNetwork = base._GetNetworkIndex(code, throwException);
					}
				}

				// Token: 0x060086AD RID: 34477 RVA: 0x0029DC80 File Offset: 0x0029BE80
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x060086AE RID: 34478 RVA: 0x0029DC88 File Offset: 0x0029BE88
				public ILogicable GetDevice(ICircuitHolder chipCircuitHousing)
				{
					int variableIndex = this.GetVariableIndex(ProgrammableChip._AliasTarget.Device, false);
					return chipCircuitHousing.GetLogicableFromIndex(variableIndex, this._DeviceNetwork);
				}

				// Token: 0x060086AF RID: 34479 RVA: 0x0029DCAC File Offset: 0x0029BEAC
				public override int GetVariableIndex(ProgrammableChip._AliasTarget type, bool throwError = true)
				{
					int result;
					if ((this._PropertiesToUse & InstructionInclude.Alias) != InstructionInclude.None && base.TryParseAliasAsIndex(type, out result))
					{
						return result;
					}
					int result2;
					if ((this._PropertiesToUse & InstructionInclude.DeviceIndex) != InstructionInclude.None && this.TryParseDeviceIndexAsIndex(out result2, throwError))
					{
						return result2;
					}
					if ((this._PropertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						return this._DeviceNetwork;
					}
					if (throwError)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return 0;
				}

				// Token: 0x060086B0 RID: 34480 RVA: 0x0029DD10 File Offset: 0x0029BF10
				protected bool TryParseDeviceIndexAsIndex(out int index, bool throwException = true)
				{
					if (this._DeviceIndex < 0 || this._DeviceRecurse < 0)
					{
						index = -1;
						return false;
					}
					int num = this._DeviceIndex;
					int deviceRecurse = this._DeviceRecurse;
					while (deviceRecurse-- > 0)
					{
						num = (int)Math.Round(this._Chip._Registers[num]);
						if (num < 0 || num >= this._Chip._Registers.Length)
						{
							if (throwException)
							{
								throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.OutOfDeviceBounds, this._LineNumber);
							}
							index = -1;
							return false;
						}
					}
					index = num;
					return true;
				}

				// Token: 0x04007471 RID: 29809
				protected readonly int _DeviceIndex = -1;

				// Token: 0x04007472 RID: 29810
				protected readonly int _DeviceRecurse = -1;

				// Token: 0x04007473 RID: 29811
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x020012EB RID: 4843
			protected class IntValuedVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x060086B1 RID: 34481 RVA: 0x0029DD90 File Offset: 0x0029BF90
				public IntValuedVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					this._IsValueSet = false;
					this._Value = -1;
					foreach (IScriptEnum scriptEnum in ProgrammableChip.InternalEnums)
					{
						scriptEnum.Execute(ref this._IsValueSet, ref this._Value, code, propertiesToUse);
					}
					if (!this._IsValueSet && (propertiesToUse & InstructionInclude.Value) != InstructionInclude.None)
					{
						foreach (ProgrammableChip.Constant constant in ProgrammableChip.AllConstants)
						{
							if (constant == code)
							{
								this._Value = (int)constant.Value;
								return;
							}
						}
						if (int.TryParse(code, out this._Value))
						{
							this._IsValueSet = true;
						}
					}
					if (!this._IsValueSet && throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidInteger, this._LineNumber);
					}
				}

				// Token: 0x060086B2 RID: 34482 RVA: 0x0029DE84 File Offset: 0x0029C084
				public int GetVariableValue(ProgrammableChip._AliasTarget type, bool throwException = true)
				{
					double num;
					if ((type & ProgrammableChip._AliasTarget.Register) != ProgrammableChip._AliasTarget.None && base.TryParseAliasAsValue(type, out num, throwException))
					{
						return (int)num;
					}
					double num2;
					if ((this._PropertiesToUse & InstructionInclude.Define) != InstructionInclude.None && this._Chip._Defines.TryGetValue(this._Alias, out num2))
					{
						return (int)num2;
					}
					int result;
					if ((this._PropertiesToUse & InstructionInclude.JumpTag) != InstructionInclude.None && base.TryParseAliasAsJumpTagValue(out result, throwException))
					{
						return result;
					}
					double a;
					if ((this._PropertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None && base.TryParseRegisterIndexAsValue(out a, throwException))
					{
						return (int)Math.Round(a);
					}
					if ((this._PropertiesToUse & (InstructionInclude.Value | InstructionInclude.Enum | InstructionInclude.LogicType | InstructionInclude.LogicSlotType | InstructionInclude.LogicReagentMode | InstructionInclude.LogicBatchMethod)) != InstructionInclude.None && this._IsValueSet)
					{
						return this._Value;
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.InvalidInteger, this._LineNumber);
					}
					return 0;
				}

				// Token: 0x04007474 RID: 29812
				private readonly bool _IsValueSet;

				// Token: 0x04007475 RID: 29813
				private readonly int _Value = -1;
			}

			// Token: 0x020012EC RID: 4844
			protected class EnumValuedVariable<T> : ProgrammableChip._Operation.ValueVariable where T : Enum, IConvertible, new()
			{
				// Token: 0x060086B3 RID: 34483 RVA: 0x0029DF30 File Offset: 0x0029C130
				public EnumValuedVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					this._IsValueSet = false;
					this._Value = -1;
					if (!this._IsValueSet && (propertiesToUse & InstructionInclude.Value) != InstructionInclude.None)
					{
						try
						{
							T typeOf = this.GetTypeOf(code);
							this._Value = typeOf.ToInt32(CultureInfo.InvariantCulture);
							this._IsValueSet = true;
							return;
						}
						catch
						{
						}
						foreach (ProgrammableChip.Constant constant in ProgrammableChip.AllConstants)
						{
							if (constant == code)
							{
								this._Value = (int)constant.Value;
								this._IsValueSet = true;
								return;
							}
						}
						if (int.TryParse(code, out this._Value))
						{
							this._IsValueSet = true;
						}
					}
				}

				// Token: 0x060086B4 RID: 34484 RVA: 0x0029DFFC File Offset: 0x0029C1FC
				private T GetTypeOf(string logicTypeCode)
				{
					if (!Enum.IsDefined(typeof(T), logicTypeCode))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
					}
					return (T)((object)Enum.Parse(typeof(T), logicTypeCode));
				}

				// Token: 0x060086B5 RID: 34485 RVA: 0x0029E034 File Offset: 0x0029C234
				public T GetVariableValue(ProgrammableChip._AliasTarget type, bool throwException = true)
				{
					double num;
					if ((type & ProgrammableChip._AliasTarget.Register) != ProgrammableChip._AliasTarget.None && base.TryParseAliasAsValue(type, out num, throwException))
					{
						return (T)((object)Enum.ToObject(typeof(T), (int)num));
					}
					double num2;
					if ((this._PropertiesToUse & InstructionInclude.Define) != InstructionInclude.None && this._Chip._Defines.TryGetValue(this._Alias, out num2))
					{
						return (T)((object)Enum.ToObject(typeof(T), (int)num2));
					}
					int value;
					if ((this._PropertiesToUse & InstructionInclude.JumpTag) != InstructionInclude.None && base.TryParseAliasAsJumpTagValue(out value, throwException))
					{
						return (T)((object)Enum.ToObject(typeof(T), value));
					}
					double num3;
					if ((this._PropertiesToUse & InstructionInclude.RegisterIndex) != InstructionInclude.None && base.TryParseRegisterIndexAsValue(out num3, throwException))
					{
						return (T)((object)Enum.ToObject(typeof(T), (int)num3));
					}
					if ((this._PropertiesToUse & InstructionInclude.Value) != InstructionInclude.None && this._IsValueSet)
					{
						return (T)((object)Enum.ToObject(typeof(T), this._Value));
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					if (throwException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectVariable, this._LineNumber);
					}
					return default(T);
				}

				// Token: 0x04007476 RID: 29814
				private readonly bool _IsValueSet;

				// Token: 0x04007477 RID: 29815
				private readonly int _Value = -1;
			}
		}

		// Token: 0x020011D6 RID: 4566
		private abstract class _Operation_I : ProgrammableChip._Operation_1_0
		{
			// Token: 0x0600849F RID: 33951 RVA: 0x0029430E File Offset: 0x0029250E
			public _Operation_I(ProgrammableChip chip, int lineNumber, string registerStoreCode, string referenceId) : base(chip, lineNumber, registerStoreCode)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400725B RID: 29275
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;
		}

		// Token: 0x020011D7 RID: 4567
		private abstract class _Operation_1_0 : ProgrammableChip._Operation
		{
			// Token: 0x060084A0 RID: 33952 RVA: 0x0029432B File Offset: 0x0029252B
			public _Operation_1_0(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber)
			{
				this._Store = new ProgrammableChip._Operation.IndexVariable(chip, lineNumber, registerStoreCode, InstructionInclude.MaskStoreIndex, false);
			}

			// Token: 0x0400725C RID: 29276
			protected ProgrammableChip._Operation.IndexVariable _Store;
		}

		// Token: 0x020011D8 RID: 4568
		private abstract class _Operation_1_1 : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084A1 RID: 33953 RVA: 0x00294345 File Offset: 0x00292545
			public _Operation_1_1(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code) : base(chip, lineNumber, registerStoreCode)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400725D RID: 29277
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x020011D9 RID: 4569
		private abstract class _Operation_1_2 : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060084A2 RID: 33954 RVA: 0x00294362 File Offset: 0x00292562
			public _Operation_1_2(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code) : base(chip, lineNumber, registerStoreCode, argument1Code)
			{
				this._Argument2 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument2Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400725E RID: 29278
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument2;
		}

		// Token: 0x020011DA RID: 4570
		private abstract class _Operation_1_3 : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084A3 RID: 33955 RVA: 0x00294381 File Offset: 0x00292581
			public _Operation_1_3(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code, string argument3Code) : base(chip, lineNumber, registerStoreCode, argument1Code, argument2Code)
			{
				this._Argument3 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument3Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400725F RID: 29279
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument3;
		}

		// Token: 0x020011DB RID: 4571
		private abstract class _Operation_J_0 : ProgrammableChip._Operation
		{
			// Token: 0x060084A4 RID: 33956 RVA: 0x002943A2 File Offset: 0x002925A2
			public _Operation_J_0(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber)
			{
				this._JumpIndex = new ProgrammableChip._Operation.LineNumberVariable(chip, lineNumber, jumpAddressCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007260 RID: 29280
			protected readonly ProgrammableChip._Operation.LineNumberVariable _JumpIndex;
		}

		// Token: 0x020011DC RID: 4572
		private abstract class _Operation_J_1 : ProgrammableChip._Operation_J_0
		{
			// Token: 0x060084A5 RID: 33957 RVA: 0x002943BD File Offset: 0x002925BD
			public _Operation_J_1(ProgrammableChip chip, int lineNumber, string argument1Code, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007261 RID: 29281
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x020011DD RID: 4573
		private abstract class _Operation_J_2 : ProgrammableChip._Operation_J_1
		{
			// Token: 0x060084A6 RID: 33958 RVA: 0x002943DA File Offset: 0x002925DA
			public _Operation_J_2(ProgrammableChip chip, int lineNumber, string argument1Code, string argument2Code, string jumpAddressCode) : base(chip, lineNumber, argument1Code, jumpAddressCode)
			{
				this._Argument2 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument2Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007262 RID: 29282
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument2;
		}

		// Token: 0x020011DE RID: 4574
		private abstract class _Operation_J_3 : ProgrammableChip._Operation_J_2
		{
			// Token: 0x060084A7 RID: 33959 RVA: 0x002943F9 File Offset: 0x002925F9
			public _Operation_J_3(ProgrammableChip chip, int lineNumber, string argument1Code, string argument2Code, string argument3Code, string jumpAddressCode) : base(chip, lineNumber, argument1Code, argument2Code, jumpAddressCode)
			{
				this._Argument3 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument3Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007263 RID: 29283
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument3;
		}

		// Token: 0x020011DF RID: 4575
		private class _L_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084A8 RID: 33960 RVA: 0x0029441A File Offset: 0x0029261A
			public _L_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicTypeCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084A9 RID: 33961 RVA: 0x0029444C File Offset: 0x0029264C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceIndex.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				LogicType variableValue = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				if (!device.CanLogicRead(variableValue))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = device.GetLogicValue(variableValue);
				return index + 1;
			}

			// Token: 0x04007264 RID: 29284
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;

			// Token: 0x04007265 RID: 29285
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E0 RID: 4576
		private class _LD_Operation : ProgrammableChip._Operation_I
		{
			// Token: 0x060084AA RID: 33962 RVA: 0x002944DC File Offset: 0x002926DC
			public _LD_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string referenceId, string logicTypeCode) : base(chip, lineNumber, registerCode, referenceId)
			{
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084AB RID: 33963 RVA: 0x00294500 File Offset: 0x00292700
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceId.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromId = this._Chip.CircuitHousing.GetLogicableFromId(variableValue, int.MinValue);
				LogicType variableValue2 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue2 == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				if (!logicableFromId.CanLogicRead(variableValue2))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = logicableFromId.GetLogicValue(variableValue2);
				return index + 1;
			}

			// Token: 0x04007266 RID: 29286
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E1 RID: 4577
		private class _S_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084AC RID: 33964 RVA: 0x0029458D File Offset: 0x0029278D
			public _S_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084AD RID: 33965 RVA: 0x002945CC File Offset: 0x002927CC
			public override int Execute(int index)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue2 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue2 == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				ILogicable device = this._DeviceIndex.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				double logicValue = device.GetLogicValue(variableValue2);
				if (!device.CanLogicWrite(variableValue2))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				if (logicValue != variableValue)
				{
					device.SetLogicValue(variableValue2, variableValue);
				}
				return index + 1;
			}

			// Token: 0x04007267 RID: 29287
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;

			// Token: 0x04007268 RID: 29288
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007269 RID: 29289
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E2 RID: 4578
		private class _SD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084AE RID: 33966 RVA: 0x0029465C File Offset: 0x0029285C
			public _SD_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084AF RID: 33967 RVA: 0x002946AC File Offset: 0x002928AC
			public override int Execute(int index)
			{
				int variableValue = this._DeviceId.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue3 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromId = this._Chip.CircuitHousing.GetLogicableFromId(variableValue, int.MinValue);
				if (logicableFromId == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				double logicValue = logicableFromId.GetLogicValue(variableValue3);
				if (!logicableFromId.CanLogicWrite(variableValue3))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				if (logicValue != variableValue2)
				{
					logicableFromId.SetLogicValue(variableValue3, variableValue2);
				}
				return index + 1;
			}

			// Token: 0x0400726A RID: 29290
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;

			// Token: 0x0400726B RID: 29291
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400726C RID: 29292
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E3 RID: 4579
		private class _SS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084B0 RID: 33968 RVA: 0x0029473C File Offset: 0x0029293C
			public _SS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string slotIndex, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotIndex, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084B1 RID: 33969 RVA: 0x00294798 File Offset: 0x00292998
			public override int Execute(int index)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicSlotType variableValue2 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue3 = this._SlotIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				ISlotWriteable slotWriteable = device as ISlotWriteable;
				if (slotWriteable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotSlotWriteable, this._LineNumber);
				}
				if (!slotWriteable.CanLogicWrite(variableValue2, variableValue3))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicSlotType, this._LineNumber);
				}
				if (device.GetLogicValue(variableValue2, variableValue3) != variableValue)
				{
					slotWriteable.SetLogicValue(variableValue2, variableValue3, variableValue);
				}
				return index + 1;
			}

			// Token: 0x0400726D RID: 29293
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400726E RID: 29294
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400726F RID: 29295
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007270 RID: 29296
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;
		}

		// Token: 0x020011E4 RID: 4580
		private class _SBS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084B2 RID: 33970 RVA: 0x0029483C File Offset: 0x00292A3C
			public _SBS_Operation(ProgrammableChip chip, int lineNumber, string deviceHash, string slotIndex, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceHash, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotIndex, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084B3 RID: 33971 RVA: 0x0029489C File Offset: 0x00292A9C
			public override int Execute(int index)
			{
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicSlotType variableValue3 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue4 = this._SlotIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue)
					{
						ISlotWriteable slotWriteable = logicable as ISlotWriteable;
						if (slotWriteable == null)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotSlotWriteable, this._LineNumber);
						}
						if (!slotWriteable.CanLogicWrite(variableValue3, variableValue4))
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicSlotType, this._LineNumber);
						}
						if (slotWriteable.GetLogicValue(variableValue3, variableValue4) != variableValue2)
						{
							slotWriteable.SetLogicValue(variableValue3, variableValue4, variableValue2);
						}
					}
				}
				return index + 1;
			}

			// Token: 0x04007271 RID: 29297
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007272 RID: 29298
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007273 RID: 29299
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007274 RID: 29300
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;
		}

		// Token: 0x020011E5 RID: 4581
		private class _LB_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084B4 RID: 33972 RVA: 0x00294980 File Offset: 0x00292B80
			public _LB_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x060084B5 RID: 33973 RVA: 0x002949D4 File Offset: 0x00292BD4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue2 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue2 == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				LogicBatchMethod variableValue3 = this._BatchMode.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue && !logicable.CanLogicRead(variableValue2))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
					}
				}
				this._Chip._Registers[variableIndex] = Device.BatchRead(variableValue3, variableValue2, variableValue, batchOutput);
				return index + 1;
			}

			// Token: 0x04007275 RID: 29301
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007276 RID: 29302
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;

			// Token: 0x04007277 RID: 29303
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x020011E6 RID: 4582
		private class _LBN_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084B6 RID: 33974 RVA: 0x00294AB0 File Offset: 0x00292CB0
			public _LBN_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string nameCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x060084B7 RID: 33975 RVA: 0x00294B14 File Offset: 0x00292D14
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._NameHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue3 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicBatchMethod variableValue4 = this._BatchMode.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue && logicable.GetNameHash() == variableValue2 && !logicable.CanLogicRead(variableValue3))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
					}
				}
				this._Chip._Registers[variableIndex] = Device.BatchRead(variableValue4, variableValue3, variableValue, variableValue2, batchOutput);
				return index + 1;
			}

			// Token: 0x04007278 RID: 29304
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007279 RID: 29305
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x0400727A RID: 29306
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;

			// Token: 0x0400727B RID: 29307
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x020011E7 RID: 4583
		private class _SB_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084B8 RID: 33976 RVA: 0x00294BF8 File Offset: 0x00292DF8
			public _SB_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084B9 RID: 33977 RVA: 0x00294C48 File Offset: 0x00292E48
			public override int Execute(int index)
			{
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue3 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue3 == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue)
					{
						if (!logicable.CanLogicWrite(variableValue3))
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
						}
						if (logicable.GetLogicValue(variableValue3) != variableValue2)
						{
							logicable.SetLogicValue(variableValue3, variableValue2);
						}
					}
				}
				return index + 1;
			}

			// Token: 0x0400727C RID: 29308
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x0400727D RID: 29309
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400727E RID: 29310
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E8 RID: 4584
		private class _SBN_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084BA RID: 33978 RVA: 0x00294D10 File Offset: 0x00292F10
			public _SBN_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string nameHash, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameHash, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060084BB RID: 33979 RVA: 0x00294D70 File Offset: 0x00292F70
			public override int Execute(int index)
			{
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._NameHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicType variableValue4 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue4 == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue && logicable.GetNameHash() == variableValue2)
					{
						if (!logicable.CanLogicWrite(variableValue4))
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
						}
						if (logicable.GetLogicValue(variableValue4) != variableValue3)
						{
							logicable.SetLogicValue(variableValue4, variableValue3);
						}
					}
				}
				return index + 1;
			}

			// Token: 0x0400727F RID: 29311
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007280 RID: 29312
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x04007281 RID: 29313
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007282 RID: 29314
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x020011E9 RID: 4585
		private class _LS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084BC RID: 33980 RVA: 0x00294E54 File Offset: 0x00293054
			public _LS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string slotCode, string logicTypeCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
			}

			// Token: 0x060084BD RID: 33981 RVA: 0x00294EA0 File Offset: 0x002930A0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._SlotIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				LogicSlotType variableValue2 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (!device.CanLogicRead(variableValue2, variableValue))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = device.GetLogicValue(variableValue2, variableValue);
				return index + 1;
			}

			// Token: 0x04007283 RID: 29315
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x04007284 RID: 29316
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x04007285 RID: 29317
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;
		}

		// Token: 0x020011EA RID: 4586
		private class _LBS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084BE RID: 33982 RVA: 0x00294F30 File Offset: 0x00293130
			public _LBS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string slotCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x060084BF RID: 33983 RVA: 0x00294F94 File Offset: 0x00293194
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._SlotIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicSlotType variableValue3 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicBatchMethod variableValue4 = this._BatchMode.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue && !logicable.CanLogicRead(variableValue3, variableValue2))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicSlotType, this._LineNumber);
					}
				}
				this._Chip._Registers[variableIndex] = Device.BatchRead(variableValue4, variableValue3, variableValue2, variableValue, batchOutput);
				return index + 1;
			}

			// Token: 0x04007286 RID: 29318
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007287 RID: 29319
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x04007288 RID: 29320
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007289 RID: 29321
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x020011EB RID: 4587
		private class _LBNS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084C0 RID: 33984 RVA: 0x00295070 File Offset: 0x00293270
			public _LBNS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string nameCode, string slotCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x060084C1 RID: 33985 RVA: 0x002950E8 File Offset: 0x002932E8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._NameHash.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue3 = this._SlotIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicSlotType variableValue4 = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				LogicBatchMethod variableValue5 = this._BatchMode.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				List<ILogicable> batchOutput = this._Chip.CircuitHousing.GetBatchOutput();
				if (batchOutput == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceListNull, this._LineNumber);
				}
				int count = batchOutput.Count;
				while (count-- > 0)
				{
					ILogicable logicable = batchOutput[count];
					if (logicable != null && logicable.GetPrefabHash() == variableValue && logicable.GetNameHash() == variableValue2 && !logicable.CanLogicRead(variableValue4, variableValue3))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicSlotType, this._LineNumber);
					}
				}
				this._Chip._Registers[variableIndex] = Device.BatchRead(variableValue5, variableValue4, variableValue3, variableValue, variableValue2, batchOutput);
				return index + 1;
			}

			// Token: 0x0400728A RID: 29322
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x0400728B RID: 29323
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x0400728C RID: 29324
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x0400728D RID: 29325
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x0400728E RID: 29326
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x020011EC RID: 4588
		private class _LR_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084C2 RID: 33986 RVA: 0x002951E0 File Offset: 0x002933E0
			public _LR_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicReagentModeCode, string reagentCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicReagentMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicReagentMode>(chip, lineNumber, logicReagentModeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicReagentMode, false);
				this._ReagentInt = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, reagentCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060084C3 RID: 33987 RVA: 0x0029522C File Offset: 0x0029342C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				LogicReagentMode variableValue = this._LogicReagentMode.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				switch (variableValue)
				{
				case LogicReagentMode.Contents:
				{
					Device device2 = device as Device;
					if (device2 == null)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentDevice, this._LineNumber);
					}
					this._Chip._Registers[variableIndex] = device2.ReadableReagentMixture.Get(Reagent.Find(this._ReagentInt.GetVariableValue(ProgrammableChip._AliasTarget.Register, true)));
					break;
				}
				case LogicReagentMode.Required:
				{
					IRequireReagent requireReagent = device as IRequireReagent;
					if (requireReagent == null)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentDevice, this._LineNumber);
					}
					this._Chip._Registers[variableIndex] = requireReagent.RequiredReagents.Get(Reagent.Find(this._ReagentInt.GetVariableValue(ProgrammableChip._AliasTarget.Register, true)));
					break;
				}
				case LogicReagentMode.Recipe:
				{
					IRequireReagent requireReagent2 = device as IRequireReagent;
					if (requireReagent2 == null)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentDevice, this._LineNumber);
					}
					this._Chip._Registers[variableIndex] = requireReagent2.CurrentRecipe.Get(Reagent.Find(this._ReagentInt.GetVariableValue(ProgrammableChip._AliasTarget.Register, true)));
					break;
				}
				case LogicReagentMode.TotalContents:
				{
					Device device3 = device as Device;
					if (device3 == null)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentDevice, this._LineNumber);
					}
					this._Chip._Registers[variableIndex] = device3.ReadableReagentMixture.TotalReagents;
					break;
				}
				default:
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.UnhandledReagentMode, this._LineNumber);
				}
				return index + 1;
			}

			// Token: 0x0400728F RID: 29327
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x04007290 RID: 29328
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicReagentMode> _LogicReagentMode;

			// Token: 0x04007291 RID: 29329
			protected readonly ProgrammableChip._Operation.IntValuedVariable _ReagentInt;
		}

		// Token: 0x020011ED RID: 4589
		private class _LABEL_Operation : ProgrammableChip._ALIAS_Operation
		{
			// Token: 0x060084C4 RID: 33988 RVA: 0x002953BE File Offset: 0x002935BE
			public _LABEL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string labelCode) : base(chip, lineNumber, labelCode, deviceCode)
			{
			}
		}

		// Token: 0x020011EE RID: 4590
		private class _ALIAS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084C5 RID: 33989 RVA: 0x002953CC File Offset: 0x002935CC
			public _ALIAS_Operation(ProgrammableChip chip, int lineNumber, string aliasCode, string targetCode) : base(chip, lineNumber)
			{
				this._AliasCode = aliasCode;
				if (targetCode[0] == 'r')
				{
					this._TargetType = ProgrammableChip._AliasTarget.Register;
					this._Target = new ProgrammableChip._Operation.IndexVariable(chip, lineNumber, targetCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.JumpTag, false);
					return;
				}
				if (targetCode[0] == 'd')
				{
					this._TargetType = ProgrammableChip._AliasTarget.Device;
					this._Target = new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, targetCode, InstructionInclude.Alias | InstructionInclude.JumpTag | InstructionInclude.DeviceIndex, false);
				}
			}

			// Token: 0x060084C6 RID: 33990 RVA: 0x00295433 File Offset: 0x00293633
			public override int Execute(int index)
			{
				return this.Execute(index, true);
			}

			// Token: 0x060084C7 RID: 33991 RVA: 0x00295440 File Offset: 0x00293640
			public int Execute(int index, bool updateLabels)
			{
				int variableIndex = this._Target.GetVariableIndex(this._TargetType, true);
				ProgrammableChip._AliasValue aliasValue = new ProgrammableChip._AliasValue(this._TargetType, variableIndex);
				if (aliasValue.Index < 0 || (aliasValue.Target == ProgrammableChip._AliasTarget.Register && aliasValue.Index >= this._Chip._Registers.Length) || (aliasValue.Target == ProgrammableChip._AliasTarget.Device && !this._Chip.CircuitHousing.IsValidIndex(aliasValue.Index)))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IndexOutOfRange, this._LineNumber);
				}
				if (this._Chip._Aliases.ContainsKey(this._AliasCode))
				{
					ProgrammableChip._AliasValue aliasValue2 = this._Chip._Aliases[this._AliasCode];
					if (this._Chip._Aliases[this._AliasCode].Target == ProgrammableChip._AliasTarget.Device)
					{
						this._Chip.CircuitHousing.SetDeviceLabel(aliasValue2.Index, "");
					}
					this._Chip._Aliases[this._AliasCode] = aliasValue;
				}
				else
				{
					this._Chip._Aliases.Add(this._AliasCode, aliasValue);
				}
				if (aliasValue.Target == ProgrammableChip._AliasTarget.Device)
				{
					this._Chip.CircuitHousing.SetDeviceLabel(aliasValue.Index, this._AliasCode);
				}
				return index + 1;
			}

			// Token: 0x04007292 RID: 29330
			private readonly string _AliasCode;

			// Token: 0x04007293 RID: 29331
			private readonly ProgrammableChip._AliasTarget _TargetType;

			// Token: 0x04007294 RID: 29332
			private readonly ProgrammableChip._Operation.IndexVariable _Target;
		}

		// Token: 0x020011EF RID: 4591
		private class _DEFINE_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060084C8 RID: 33992 RVA: 0x00295584 File Offset: 0x00293784
			public _DEFINE_Operation(ProgrammableChip chip, int lineNumber, string defineCode, string floatCode) : base(chip, lineNumber)
			{
				ProgrammableChip._Operation.DoubleValueVariable doubleValueVariable = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, floatCode, InstructionInclude.MaskDefineValue, true);
				if (chip._Defines.ContainsKey(defineCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ExtraDefine, lineNumber);
				}
				chip._Defines.Add(defineCode, doubleValueVariable.Get());
			}

			// Token: 0x060084C9 RID: 33993 RVA: 0x002955CF File Offset: 0x002937CF
			public override int Execute(int index)
			{
				return index + 1;
			}
		}

		// Token: 0x020011F0 RID: 4592
		private class _MOVE_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060084CA RID: 33994 RVA: 0x002955D4 File Offset: 0x002937D4
			public _MOVE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060084CB RID: 33995 RVA: 0x002955E4 File Offset: 0x002937E4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue;
				return index + 1;
			}
		}

		// Token: 0x020011F1 RID: 4593
		private class _ADD_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084CC RID: 33996 RVA: 0x0029561E File Offset: 0x0029381E
			public _ADD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084CD RID: 33997 RVA: 0x00295630 File Offset: 0x00293830
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue + variableValue2;
				return index + 1;
			}
		}

		// Token: 0x020011F2 RID: 4594
		private class _SUB_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084CE RID: 33998 RVA: 0x0029567A File Offset: 0x0029387A
			public _SUB_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084CF RID: 33999 RVA: 0x0029568C File Offset: 0x0029388C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue - variableValue2;
				return index + 1;
			}
		}

		// Token: 0x020011F3 RID: 4595
		private class _SDSE_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084D0 RID: 34000 RVA: 0x002956D6 File Offset: 0x002938D6
			public _SDSE_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x060084D1 RID: 34001 RVA: 0x002956F0 File Offset: 0x002938F0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				this._Chip._Registers[variableIndex] = (double)((device == null) ? 0f : 1f);
				return index + 1;
			}

			// Token: 0x04007295 RID: 29333
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;
		}

		// Token: 0x020011F4 RID: 4596
		private class _SDNS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060084D2 RID: 34002 RVA: 0x00295742 File Offset: 0x00293942
			public _SDNS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x060084D3 RID: 34003 RVA: 0x0029575C File Offset: 0x0029395C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				this._Chip._Registers[variableIndex] = (double)((device == null) ? 1f : 0f);
				return index + 1;
			}

			// Token: 0x04007296 RID: 29334
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;
		}

		// Token: 0x020011F5 RID: 4597
		private class _SLT_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084D4 RID: 34004 RVA: 0x002957AE File Offset: 0x002939AE
			public _SLT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084D5 RID: 34005 RVA: 0x002957C0 File Offset: 0x002939C0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue < variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011F6 RID: 4598
		private class _SGT_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084D6 RID: 34006 RVA: 0x0029581F File Offset: 0x00293A1F
			public _SGT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084D7 RID: 34007 RVA: 0x00295830 File Offset: 0x00293A30
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue > variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011F7 RID: 4599
		private class _SLE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084D8 RID: 34008 RVA: 0x0029588F File Offset: 0x00293A8F
			public _SLE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084D9 RID: 34009 RVA: 0x002958A0 File Offset: 0x00293AA0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue <= variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011F8 RID: 4600
		private class _SGE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084DA RID: 34010 RVA: 0x002958FF File Offset: 0x00293AFF
			public _SGE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084DB RID: 34011 RVA: 0x00295910 File Offset: 0x00293B10
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue >= variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011F9 RID: 4601
		private class _SEQ_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084DC RID: 34012 RVA: 0x0029596F File Offset: 0x00293B6F
			public _SEQ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084DD RID: 34013 RVA: 0x00295980 File Offset: 0x00293B80
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue == variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011FA RID: 4602
		private class _SNE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084DE RID: 34014 RVA: 0x002959DF File Offset: 0x00293BDF
			public _SNE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084DF RID: 34015 RVA: 0x002959F0 File Offset: 0x00293BF0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue != variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011FB RID: 4603
		private class _SAP_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x060084E0 RID: 34016 RVA: 0x00295A4F File Offset: 0x00293C4F
			public _SAP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x060084E1 RID: 34017 RVA: 0x00295A60 File Offset: 0x00293C60
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((Math.Abs(variableValue - variableValue2) <= Math.Max(variableValue3 * Math.Max(Math.Abs(variableValue), Math.Abs(variableValue2)), 1.1210387714598537E-44)) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011FC RID: 4604
		private class _SNA_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x060084E2 RID: 34018 RVA: 0x00295AF4 File Offset: 0x00293CF4
			public _SNA_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x060084E3 RID: 34019 RVA: 0x00295B08 File Offset: 0x00293D08
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((Math.Abs(variableValue - variableValue2) > Math.Max(variableValue3 * Math.Max(Math.Abs(variableValue), Math.Abs(variableValue2)), 1.1210387714598537E-44)) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011FD RID: 4605
		private class _SNAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060084E4 RID: 34020 RVA: 0x00295B9C File Offset: 0x00293D9C
			public _SNAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060084E5 RID: 34021 RVA: 0x00295BAC File Offset: 0x00293DAC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = (double.IsNaN(variableValue) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020011FE RID: 4606
		private class _SNANZ_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060084E6 RID: 34022 RVA: 0x00295C01 File Offset: 0x00293E01
			public _SNANZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060084E7 RID: 34023 RVA: 0x00295C10 File Offset: 0x00293E10
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = (double.IsNaN(variableValue) ? 0.0 : 1.0);
				return index + 1;
			}
		}

		// Token: 0x020011FF RID: 4607
		private class _SLTZ_Operation : ProgrammableChip._SLT_Operation
		{
			// Token: 0x060084E8 RID: 34024 RVA: 0x00295C65 File Offset: 0x00293E65
			public _SLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001200 RID: 4608
		private class _SGTZ_Operation : ProgrammableChip._SGT_Operation
		{
			// Token: 0x060084E9 RID: 34025 RVA: 0x00295C77 File Offset: 0x00293E77
			public _SGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001201 RID: 4609
		private class _SLEZ_Operation : ProgrammableChip._SLE_Operation
		{
			// Token: 0x060084EA RID: 34026 RVA: 0x00295C89 File Offset: 0x00293E89
			public _SLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001202 RID: 4610
		private class _SGEZ_Operation : ProgrammableChip._SGE_Operation
		{
			// Token: 0x060084EB RID: 34027 RVA: 0x00295C9B File Offset: 0x00293E9B
			public _SGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001203 RID: 4611
		private class _SEQZ_Operation : ProgrammableChip._SEQ_Operation
		{
			// Token: 0x060084EC RID: 34028 RVA: 0x00295CAD File Offset: 0x00293EAD
			public _SEQZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001204 RID: 4612
		private class _SNEZ_Operation : ProgrammableChip._SNE_Operation
		{
			// Token: 0x060084ED RID: 34029 RVA: 0x00295CBF File Offset: 0x00293EBF
			public _SNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x02001205 RID: 4613
		private class _SAPZ_Operation : ProgrammableChip._SAP_Operation
		{
			// Token: 0x060084EE RID: 34030 RVA: 0x00295CD1 File Offset: 0x00293ED1
			public _SAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0", registerArgument2Code)
			{
			}
		}

		// Token: 0x02001206 RID: 4614
		private class _SNAZ_Operation : ProgrammableChip._SNA_Operation
		{
			// Token: 0x060084EF RID: 34031 RVA: 0x00295CE5 File Offset: 0x00293EE5
			public _SNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0", registerArgument2Code)
			{
			}
		}

		// Token: 0x02001207 RID: 4615
		private class _EXT_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x060084F0 RID: 34032 RVA: 0x00295CF9 File Offset: 0x00293EF9
			public _EXT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string arg1Code, string arg2Code, string arg3Code) : base(chip, lineNumber, registerStoreCode, arg1Code, arg2Code, arg3Code)
			{
			}

			// Token: 0x060084F1 RID: 34033 RVA: 0x00295D0C File Offset: 0x00293F0C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ulong variableLong = (ulong)this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				int variableInt2 = this._Argument3.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				if (variableInt2 <= 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
				}
				if (variableInt < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
				}
				if (variableInt >= 53)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftOverflow, this._LineNumber);
				}
				if (variableInt2 > 53 || variableInt + variableInt2 > 53)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.PayloadOverflow, this._LineNumber);
				}
				ulong num = variableLong & 9007199254740991UL;
				ulong num2 = ((variableInt2 == 53) ? 9007199254740991UL : ((1UL << variableInt2) - 1UL)) << variableInt;
				ulong l = (num & num2) >> variableInt;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble((long)l);
				return index + 1;
			}
		}

		// Token: 0x02001208 RID: 4616
		private class _INS_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x060084F2 RID: 34034 RVA: 0x00295DEC File Offset: 0x00293FEC
			public _INS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string arg1Code, string arg2Code, string arg3Code) : base(chip, lineNumber, registerStoreCode, arg1Code, arg2Code, arg3Code)
			{
			}

			// Token: 0x060084F3 RID: 34035 RVA: 0x00295E00 File Offset: 0x00294000
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableInt = this._Argument1.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				int variableInt2 = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument3.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true);
				if (variableInt2 <= 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
				}
				if (variableInt < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftUnderflow, this._LineNumber);
				}
				if (variableInt >= 53)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ShiftOverflow, this._LineNumber);
				}
				if (variableInt2 > 53 || variableInt + variableInt2 > 53)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.PayloadOverflow, this._LineNumber);
				}
				ulong num = 9007199254740991UL;
				ulong num2 = (ulong)(ProgrammableChip.DoubleToLong(this._Chip._Registers[variableIndex], false) & (long)num);
				ulong num3 = (ulong)(variableLong & (long)num);
				ulong num4 = (variableInt2 == 53) ? num : ((1UL << variableInt2) - 1UL);
				ulong num5 = num4 << variableInt;
				ulong num6 = num2 & ~num5;
				ulong num7 = (num3 & num4) << variableInt & num5;
				ulong l = (num6 | num7) & num;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble((long)l);
				return index + 1;
			}
		}

		// Token: 0x02001209 RID: 4617
		private class _AND_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084F4 RID: 34036 RVA: 0x00295F0E File Offset: 0x0029410E
			public _AND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084F5 RID: 34037 RVA: 0x00295F20 File Offset: 0x00294120
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long variableLong2 = this._Argument2.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong & variableLong2);
				return index + 1;
			}
		}

		// Token: 0x0200120A RID: 4618
		private class _OR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084F6 RID: 34038 RVA: 0x00295F71 File Offset: 0x00294171
			public _OR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084F7 RID: 34039 RVA: 0x00295F80 File Offset: 0x00294180
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long variableLong2 = this._Argument2.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long num = variableLong;
				long num2 = variableLong2;
				long l = num | num2;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x0200120B RID: 4619
		private class _XOR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084F8 RID: 34040 RVA: 0x00295FD7 File Offset: 0x002941D7
			public _XOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084F9 RID: 34041 RVA: 0x00295FE8 File Offset: 0x002941E8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long variableLong2 = this._Argument2.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long num = variableLong;
				long num2 = variableLong2;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(num ^ num2);
				return index + 1;
			}
		}

		// Token: 0x0200120C RID: 4620
		private class _NOR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084FA RID: 34042 RVA: 0x0029603B File Offset: 0x0029423B
			public _NOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084FB RID: 34043 RVA: 0x0029604C File Offset: 0x0029424C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long variableLong2 = this._Argument2.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long num = variableLong;
				long num2 = variableLong2;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(~(num | num2));
				return index + 1;
			}
		}

		// Token: 0x0200120D RID: 4621
		private class _NOT_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060084FC RID: 34044 RVA: 0x002960A0 File Offset: 0x002942A0
			public _NOT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgumentCode) : base(chip, lineNumber, registerStoreCode, registerArgumentCode)
			{
			}

			// Token: 0x060084FD RID: 34045 RVA: 0x002960B0 File Offset: 0x002942B0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long l = ~this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x0200120E RID: 4622
		private class _MUL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060084FE RID: 34046 RVA: 0x002960F1 File Offset: 0x002942F1
			public _MUL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060084FF RID: 34047 RVA: 0x00296100 File Offset: 0x00294300
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue * variableValue2;
				return index + 1;
			}
		}

		// Token: 0x0200120F RID: 4623
		private class _DIV_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008500 RID: 34048 RVA: 0x0029614A File Offset: 0x0029434A
			public _DIV_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008501 RID: 34049 RVA: 0x0029615C File Offset: 0x0029435C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue / variableValue2;
				return index + 1;
			}
		}

		// Token: 0x02001210 RID: 4624
		private class _MOD_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008502 RID: 34050 RVA: 0x002961A6 File Offset: 0x002943A6
			public _MOD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008503 RID: 34051 RVA: 0x002961B8 File Offset: 0x002943B8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double num = variableValue;
				double num2 = variableValue2;
				double num3 = num % num2;
				if (num3 < 0.0)
				{
					num3 += num2;
				}
				this._Chip._Registers[variableIndex] = num3;
				return index + 1;
			}
		}

		// Token: 0x02001211 RID: 4625
		private class _J_Operation : ProgrammableChip._JR_Operation
		{
			// Token: 0x06008504 RID: 34052 RVA: 0x0029621B File Offset: 0x0029441B
			public _J_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008505 RID: 34053 RVA: 0x00296226 File Offset: 0x00294426
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001212 RID: 4626
		private class _BLT_Operation : ProgrammableChip._BRLT_Operation
		{
			// Token: 0x06008506 RID: 34054 RVA: 0x00296231 File Offset: 0x00294431
			public _BLT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008507 RID: 34055 RVA: 0x00296240 File Offset: 0x00294440
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001213 RID: 4627
		private class _BGT_Operation : ProgrammableChip._BRGT_Operation
		{
			// Token: 0x06008508 RID: 34056 RVA: 0x0029624B File Offset: 0x0029444B
			public _BGT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008509 RID: 34057 RVA: 0x0029625A File Offset: 0x0029445A
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001214 RID: 4628
		private class _BLE_Operation : ProgrammableChip._BRLE_Operation
		{
			// Token: 0x0600850A RID: 34058 RVA: 0x00296265 File Offset: 0x00294465
			public _BLE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600850B RID: 34059 RVA: 0x00296274 File Offset: 0x00294474
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001215 RID: 4629
		private class _BGE_Operation : ProgrammableChip._BRGE_Operation
		{
			// Token: 0x0600850C RID: 34060 RVA: 0x0029627F File Offset: 0x0029447F
			public _BGE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600850D RID: 34061 RVA: 0x0029628E File Offset: 0x0029448E
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001216 RID: 4630
		private class _BLTZ_Operation : ProgrammableChip._BLT_Operation
		{
			// Token: 0x0600850E RID: 34062 RVA: 0x00296299 File Offset: 0x00294499
			public _BLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001217 RID: 4631
		private class _BGEZ_Operation : ProgrammableChip._BGE_Operation
		{
			// Token: 0x0600850F RID: 34063 RVA: 0x002962AB File Offset: 0x002944AB
			public _BGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001218 RID: 4632
		private class _BLEZ_Operation : ProgrammableChip._BLE_Operation
		{
			// Token: 0x06008510 RID: 34064 RVA: 0x002962BD File Offset: 0x002944BD
			public _BLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001219 RID: 4633
		private class _BGTZ_Operation : ProgrammableChip._BGT_Operation
		{
			// Token: 0x06008511 RID: 34065 RVA: 0x002962CF File Offset: 0x002944CF
			public _BGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200121A RID: 4634
		private class _BDSE_Operation : ProgrammableChip._BRDSE_Operation
		{
			// Token: 0x06008512 RID: 34066 RVA: 0x002962E1 File Offset: 0x002944E1
			public _BDSE_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008513 RID: 34067 RVA: 0x002962EE File Offset: 0x002944EE
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x0200121B RID: 4635
		private class _BDNS_Operation : ProgrammableChip._BRDNS_Operation
		{
			// Token: 0x06008514 RID: 34068 RVA: 0x002962F9 File Offset: 0x002944F9
			public _BDNS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008515 RID: 34069 RVA: 0x00296306 File Offset: 0x00294506
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x0200121C RID: 4636
		private class _BEQ_Operation : ProgrammableChip._BREQ_Operation
		{
			// Token: 0x06008516 RID: 34070 RVA: 0x00296311 File Offset: 0x00294511
			public _BEQ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008517 RID: 34071 RVA: 0x00296320 File Offset: 0x00294520
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x0200121D RID: 4637
		private class _BNE_Operation : ProgrammableChip._BRNE_Operation
		{
			// Token: 0x06008518 RID: 34072 RVA: 0x0029632B File Offset: 0x0029452B
			public _BNE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008519 RID: 34073 RVA: 0x0029633A File Offset: 0x0029453A
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x0200121E RID: 4638
		private class _BAP_Operation : ProgrammableChip._BRAP_Operation
		{
			// Token: 0x0600851A RID: 34074 RVA: 0x00296345 File Offset: 0x00294545
			public _BAP_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x0600851B RID: 34075 RVA: 0x00296356 File Offset: 0x00294556
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x0200121F RID: 4639
		private class _BNA_Operation : ProgrammableChip._BRNA_Operation
		{
			// Token: 0x0600851C RID: 34076 RVA: 0x00296361 File Offset: 0x00294561
			public _BNA_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x0600851D RID: 34077 RVA: 0x00296372 File Offset: 0x00294572
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001220 RID: 4640
		private class _BEQZ_Operation : ProgrammableChip._BEQ_Operation
		{
			// Token: 0x0600851E RID: 34078 RVA: 0x0029637D File Offset: 0x0029457D
			public _BEQZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001221 RID: 4641
		private class _BNEZ_Operation : ProgrammableChip._BNE_Operation
		{
			// Token: 0x0600851F RID: 34079 RVA: 0x0029638F File Offset: 0x0029458F
			public _BNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001222 RID: 4642
		private class _BAPZ_Operation : ProgrammableChip._BAP_Operation
		{
			// Token: 0x06008520 RID: 34080 RVA: 0x002963A1 File Offset: 0x002945A1
			public _BAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x02001223 RID: 4643
		private class _BNAZ_Operation : ProgrammableChip._BNA_Operation
		{
			// Token: 0x06008521 RID: 34081 RVA: 0x002963B5 File Offset: 0x002945B5
			public _BNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x02001224 RID: 4644
		private class _JAL_Operation : ProgrammableChip._J_Operation
		{
			// Token: 0x06008522 RID: 34082 RVA: 0x002963C9 File Offset: 0x002945C9
			public _JAL_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008523 RID: 34083 RVA: 0x002963D4 File Offset: 0x002945D4
			public override int Execute(int index)
			{
				this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				return base.Execute(index);
			}
		}

		// Token: 0x02001225 RID: 4645
		private class _BDSEAL_Operation : ProgrammableChip._BDSE_Operation
		{
			// Token: 0x06008524 RID: 34084 RVA: 0x002963F8 File Offset: 0x002945F8
			public _BDSEAL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008525 RID: 34085 RVA: 0x00296408 File Offset: 0x00294608
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001226 RID: 4646
		private class _BDNSAL_Operation : ProgrammableChip._BDNS_Operation
		{
			// Token: 0x06008526 RID: 34086 RVA: 0x0029643E File Offset: 0x0029463E
			public _BDNSAL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008527 RID: 34087 RVA: 0x0029644C File Offset: 0x0029464C
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001227 RID: 4647
		private class _BLTAL_Operation : ProgrammableChip._BLT_Operation
		{
			// Token: 0x06008528 RID: 34088 RVA: 0x00296482 File Offset: 0x00294682
			public _BLTAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008529 RID: 34089 RVA: 0x00296494 File Offset: 0x00294694
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001228 RID: 4648
		private class _BGEAL_Operation : ProgrammableChip._BGE_Operation
		{
			// Token: 0x0600852A RID: 34090 RVA: 0x002964CA File Offset: 0x002946CA
			public _BGEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600852B RID: 34091 RVA: 0x002964DC File Offset: 0x002946DC
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001229 RID: 4649
		private class _BLEAL_Operation : ProgrammableChip._BLE_Operation
		{
			// Token: 0x0600852C RID: 34092 RVA: 0x00296512 File Offset: 0x00294712
			public _BLEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600852D RID: 34093 RVA: 0x00296524 File Offset: 0x00294724
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x0200122A RID: 4650
		private class _BGTAL_Operation : ProgrammableChip._BGT_Operation
		{
			// Token: 0x0600852E RID: 34094 RVA: 0x0029655A File Offset: 0x0029475A
			public _BGTAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600852F RID: 34095 RVA: 0x0029656C File Offset: 0x0029476C
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x0200122B RID: 4651
		private class _BLTZAL_Operation : ProgrammableChip._BLTAL_Operation
		{
			// Token: 0x06008530 RID: 34096 RVA: 0x002965A2 File Offset: 0x002947A2
			public _BLTZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200122C RID: 4652
		private class _BGEZAL_Operation : ProgrammableChip._BGEAL_Operation
		{
			// Token: 0x06008531 RID: 34097 RVA: 0x002965B4 File Offset: 0x002947B4
			public _BGEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200122D RID: 4653
		private class _BLEZAL_Operation : ProgrammableChip._BLEAL_Operation
		{
			// Token: 0x06008532 RID: 34098 RVA: 0x002965C6 File Offset: 0x002947C6
			public _BLEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200122E RID: 4654
		private class _BGTZAL_Operation : ProgrammableChip._BGTAL_Operation
		{
			// Token: 0x06008533 RID: 34099 RVA: 0x002965D8 File Offset: 0x002947D8
			public _BGTZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200122F RID: 4655
		private class _BEQAL_Operation : ProgrammableChip._BEQ_Operation
		{
			// Token: 0x06008534 RID: 34100 RVA: 0x002965EA File Offset: 0x002947EA
			public _BEQAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008535 RID: 34101 RVA: 0x002965FC File Offset: 0x002947FC
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001230 RID: 4656
		private class _BNEAL_Operation : ProgrammableChip._BNE_Operation
		{
			// Token: 0x06008536 RID: 34102 RVA: 0x00296632 File Offset: 0x00294832
			public _BNEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008537 RID: 34103 RVA: 0x00296644 File Offset: 0x00294844
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001231 RID: 4657
		private class _BAPAL_Operation : ProgrammableChip._BAP_Operation
		{
			// Token: 0x06008538 RID: 34104 RVA: 0x0029667A File Offset: 0x0029487A
			public _BAPAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008539 RID: 34105 RVA: 0x0029668C File Offset: 0x0029488C
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001232 RID: 4658
		private class _BNAAL_Operation : ProgrammableChip._BNA_Operation
		{
			// Token: 0x0600853A RID: 34106 RVA: 0x002966C2 File Offset: 0x002948C2
			public _BNAAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x0600853B RID: 34107 RVA: 0x002966D4 File Offset: 0x002948D4
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001233 RID: 4659
		private class _BEQZAL_Operation : ProgrammableChip._BEQAL_Operation
		{
			// Token: 0x0600853C RID: 34108 RVA: 0x0029670A File Offset: 0x0029490A
			public _BEQZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001234 RID: 4660
		private class _BNEZAL_Operation : ProgrammableChip._BNEAL_Operation
		{
			// Token: 0x0600853D RID: 34109 RVA: 0x0029671C File Offset: 0x0029491C
			public _BNEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001235 RID: 4661
		private class _BAPZAL_Operation : ProgrammableChip._BAPAL_Operation
		{
			// Token: 0x0600853E RID: 34110 RVA: 0x0029672E File Offset: 0x0029492E
			public _BAPZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x02001236 RID: 4662
		private class _BNAZAL_Operation : ProgrammableChip._BNAAL_Operation
		{
			// Token: 0x0600853F RID: 34111 RVA: 0x00296742 File Offset: 0x00294942
			public _BNAZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008540 RID: 34112 RVA: 0x00296758 File Offset: 0x00294958
			public override int Execute(int index)
			{
				bool flag;
				int result = base.Execute(index, out flag, -index);
				if (flag)
				{
					this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				}
				return result;
			}
		}

		// Token: 0x02001237 RID: 4663
		private class _JR_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008541 RID: 34113 RVA: 0x0029678E File Offset: 0x0029498E
			public _JR_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008542 RID: 34114 RVA: 0x00296799 File Offset: 0x00294999
			public override int Execute(int index)
			{
				return this.Execute(index, 0);
			}

			// Token: 0x06008543 RID: 34115 RVA: 0x002967A3 File Offset: 0x002949A3
			public int Execute(int index, int offset)
			{
				return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
			}
		}

		// Token: 0x02001238 RID: 4664
		private class _BRLT_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008544 RID: 34116 RVA: 0x002967B6 File Offset: 0x002949B6
			public _BRLT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008545 RID: 34117 RVA: 0x002967C8 File Offset: 0x002949C8
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008546 RID: 34118 RVA: 0x002967E0 File Offset: 0x002949E0
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008547 RID: 34119 RVA: 0x002967F8 File Offset: 0x002949F8
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue < variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001239 RID: 4665
		private class _BRGT_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008548 RID: 34120 RVA: 0x0029683E File Offset: 0x00294A3E
			public _BRGT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008549 RID: 34121 RVA: 0x00296850 File Offset: 0x00294A50
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600854A RID: 34122 RVA: 0x00296868 File Offset: 0x00294A68
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600854B RID: 34123 RVA: 0x00296880 File Offset: 0x00294A80
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue > variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x0200123A RID: 4666
		private class _BRLE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x0600854C RID: 34124 RVA: 0x002968C6 File Offset: 0x00294AC6
			public _BRLE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x0600854D RID: 34125 RVA: 0x002968D8 File Offset: 0x00294AD8
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600854E RID: 34126 RVA: 0x002968F0 File Offset: 0x00294AF0
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600854F RID: 34127 RVA: 0x00296908 File Offset: 0x00294B08
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue <= variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x0200123B RID: 4667
		private class _BRGE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008550 RID: 34128 RVA: 0x0029694E File Offset: 0x00294B4E
			public _BRGE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008551 RID: 34129 RVA: 0x00296960 File Offset: 0x00294B60
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008552 RID: 34130 RVA: 0x00296978 File Offset: 0x00294B78
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008553 RID: 34131 RVA: 0x00296990 File Offset: 0x00294B90
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue >= variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x0200123C RID: 4668
		private class _BRLTZ_Operation : ProgrammableChip._BRLT_Operation
		{
			// Token: 0x06008554 RID: 34132 RVA: 0x002969D6 File Offset: 0x00294BD6
			public _BRLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200123D RID: 4669
		private class _BRGEZ_Operation : ProgrammableChip._BRGE_Operation
		{
			// Token: 0x06008555 RID: 34133 RVA: 0x002969E8 File Offset: 0x00294BE8
			public _BRGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200123E RID: 4670
		private class _BRLEZ_Operation : ProgrammableChip._BRLE_Operation
		{
			// Token: 0x06008556 RID: 34134 RVA: 0x002969FA File Offset: 0x00294BFA
			public _BRLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200123F RID: 4671
		private class _BRGTZ_Operation : ProgrammableChip._BRGT_Operation
		{
			// Token: 0x06008557 RID: 34135 RVA: 0x00296A0C File Offset: 0x00294C0C
			public _BRGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001240 RID: 4672
		private class _BRDSE_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008558 RID: 34136 RVA: 0x00296A1E File Offset: 0x00294C1E
			public _BRDSE_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x06008559 RID: 34137 RVA: 0x00296A38 File Offset: 0x00294C38
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600855A RID: 34138 RVA: 0x00296A50 File Offset: 0x00294C50
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600855B RID: 34139 RVA: 0x00296A67 File Offset: 0x00294C67
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				if (this._DeviceIndex.GetDevice(this._Chip.CircuitHousing) == null)
				{
					hasJumped = false;
					return index + 1;
				}
				hasJumped = true;
				return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
			}

			// Token: 0x04007297 RID: 29335
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;
		}

		// Token: 0x02001241 RID: 4673
		private class _BRDNS_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x0600855C RID: 34140 RVA: 0x00296A9C File Offset: 0x00294C9C
			public _BRDNS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x0600855D RID: 34141 RVA: 0x00296AB8 File Offset: 0x00294CB8
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600855E RID: 34142 RVA: 0x00296AD0 File Offset: 0x00294CD0
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600855F RID: 34143 RVA: 0x00296AE7 File Offset: 0x00294CE7
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				if (this._DeviceIndex.GetDevice(this._Chip.CircuitHousing) == null)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}

			// Token: 0x04007298 RID: 29336
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;
		}

		// Token: 0x02001242 RID: 4674
		private class _BREQ_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008560 RID: 34144 RVA: 0x00296B1C File Offset: 0x00294D1C
			public _BREQ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008561 RID: 34145 RVA: 0x00296B2C File Offset: 0x00294D2C
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008562 RID: 34146 RVA: 0x00296B44 File Offset: 0x00294D44
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008563 RID: 34147 RVA: 0x00296B5C File Offset: 0x00294D5C
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue == variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001243 RID: 4675
		private class _BRNE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008564 RID: 34148 RVA: 0x00296BA2 File Offset: 0x00294DA2
			public _BRNE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008565 RID: 34149 RVA: 0x00296BB4 File Offset: 0x00294DB4
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008566 RID: 34150 RVA: 0x00296BCC File Offset: 0x00294DCC
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008567 RID: 34151 RVA: 0x00296BE4 File Offset: 0x00294DE4
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue != variableValue2)
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001244 RID: 4676
		private class _BRAP_Operation : ProgrammableChip._Operation_J_3
		{
			// Token: 0x06008568 RID: 34152 RVA: 0x00296C2A File Offset: 0x00294E2A
			public _BRAP_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008569 RID: 34153 RVA: 0x00296C3C File Offset: 0x00294E3C
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600856A RID: 34154 RVA: 0x00296C54 File Offset: 0x00294E54
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600856B RID: 34155 RVA: 0x00296C6C File Offset: 0x00294E6C
			public int Execute(int index, out bool hasJumped, int offset)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (Math.Abs(variableValue - variableValue2) <= Math.Max(variableValue3 * Math.Max(Math.Abs(variableValue), Math.Abs(variableValue2)), 1.1210387714598537E-44))
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001245 RID: 4677
		private class _BRNA_Operation : ProgrammableChip._Operation_J_3
		{
			// Token: 0x0600856C RID: 34156 RVA: 0x00296CE9 File Offset: 0x00294EE9
			public _BRNA_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x0600856D RID: 34157 RVA: 0x00296CFC File Offset: 0x00294EFC
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x0600856E RID: 34158 RVA: 0x00296D14 File Offset: 0x00294F14
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x0600856F RID: 34159 RVA: 0x00296D2C File Offset: 0x00294F2C
			public int Execute(int index, out bool hasJumped, int offset)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (Math.Abs(variableValue - variableValue2) > Math.Max(variableValue3 * Math.Max(Math.Abs(variableValue), Math.Abs(variableValue2)), 1.1210387714598537E-44))
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001246 RID: 4678
		private class _BREQZ_Operation : ProgrammableChip._BREQ_Operation
		{
			// Token: 0x06008570 RID: 34160 RVA: 0x00296DA9 File Offset: 0x00294FA9
			public _BREQZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x02001247 RID: 4679
		private class _BNAN_Operation : ProgrammableChip._BRNAN_Operation
		{
			// Token: 0x06008571 RID: 34161 RVA: 0x00296DBB File Offset: 0x00294FBB
			public _BNAN_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, jumpAddressCode)
			{
			}

			// Token: 0x06008572 RID: 34162 RVA: 0x00296DC8 File Offset: 0x00294FC8
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x02001248 RID: 4680
		private class _BRNAN_Operation : ProgrammableChip._Operation_J_1
		{
			// Token: 0x06008573 RID: 34163 RVA: 0x00296DD3 File Offset: 0x00294FD3
			public _BRNAN_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, jumpAddressCode)
			{
			}

			// Token: 0x06008574 RID: 34164 RVA: 0x00296DE0 File Offset: 0x00294FE0
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008575 RID: 34165 RVA: 0x00296DF8 File Offset: 0x00294FF8
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008576 RID: 34166 RVA: 0x00296E0F File Offset: 0x0029500F
			public int Execute(int index, out bool hasJumped, int offset = 0)
			{
				if (double.IsNaN(this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true)))
				{
					hasJumped = true;
					return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				hasJumped = false;
				return index + 1;
			}
		}

		// Token: 0x02001249 RID: 4681
		private class _BRNEZ_Operation : ProgrammableChip._BRNE_Operation
		{
			// Token: 0x06008577 RID: 34167 RVA: 0x00296E40 File Offset: 0x00295040
			public _BRNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x0200124A RID: 4682
		private class _BRAPZ_Operation : ProgrammableChip._BRAP_Operation
		{
			// Token: 0x06008578 RID: 34168 RVA: 0x00296E52 File Offset: 0x00295052
			public _BRAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x0200124B RID: 4683
		private class _BRNAZ_Operation : ProgrammableChip._BRNA_Operation
		{
			// Token: 0x06008579 RID: 34169 RVA: 0x00296E66 File Offset: 0x00295066
			public _BRNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x0200124C RID: 4684
		private class _SQRT_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x0600857A RID: 34170 RVA: 0x00296E7A File Offset: 0x0029507A
			public _SQRT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x0600857B RID: 34171 RVA: 0x00296E88 File Offset: 0x00295088
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Sqrt(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200124D RID: 4685
		private class _ROUND_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x0600857C RID: 34172 RVA: 0x00296EC7 File Offset: 0x002950C7
			public _ROUND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x0600857D RID: 34173 RVA: 0x00296ED4 File Offset: 0x002950D4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Round(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200124E RID: 4686
		private class _TRUNC_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x0600857E RID: 34174 RVA: 0x00296F13 File Offset: 0x00295113
			public _TRUNC_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x0600857F RID: 34175 RVA: 0x00296F20 File Offset: 0x00295120
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Truncate(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200124F RID: 4687
		private class _CEIL_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008580 RID: 34176 RVA: 0x00296F5F File Offset: 0x0029515F
			public _CEIL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008581 RID: 34177 RVA: 0x00296F6C File Offset: 0x0029516C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Ceiling(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001250 RID: 4688
		private class _FLOOR_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008582 RID: 34178 RVA: 0x00296FAB File Offset: 0x002951AB
			public _FLOOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008583 RID: 34179 RVA: 0x00296FB8 File Offset: 0x002951B8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Floor(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001251 RID: 4689
		private class _MAX_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008584 RID: 34180 RVA: 0x00296FF7 File Offset: 0x002951F7
			public _MAX_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008585 RID: 34181 RVA: 0x00297008 File Offset: 0x00295208
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Max(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x02001252 RID: 4690
		private class _MIN_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008586 RID: 34182 RVA: 0x00297056 File Offset: 0x00295256
			public _MIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008587 RID: 34183 RVA: 0x00297068 File Offset: 0x00295268
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Min(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x02001253 RID: 4691
		private class _POW_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008588 RID: 34184 RVA: 0x002970B6 File Offset: 0x002952B6
			public _POW_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008589 RID: 34185 RVA: 0x002970C8 File Offset: 0x002952C8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Pow(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x02001254 RID: 4692
		private class _LERP_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x0600858A RID: 34186 RVA: 0x00297116 File Offset: 0x00295316
			public _LERP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x0600858B RID: 34187 RVA: 0x00297128 File Offset: 0x00295328
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = RocketMath.Lerp(variableValue, variableValue2, variableValue3);
				return index + 1;
			}
		}

		// Token: 0x02001255 RID: 4693
		private class _ABS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x0600858C RID: 34188 RVA: 0x00297185 File Offset: 0x00295385
			public _ABS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x0600858D RID: 34189 RVA: 0x00297194 File Offset: 0x00295394
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Abs(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001256 RID: 4694
		private class _LOG_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x0600858E RID: 34190 RVA: 0x002971D3 File Offset: 0x002953D3
			public _LOG_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x0600858F RID: 34191 RVA: 0x002971E0 File Offset: 0x002953E0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Log(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001257 RID: 4695
		private class _EXP_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008590 RID: 34192 RVA: 0x0029721F File Offset: 0x0029541F
			public _EXP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008591 RID: 34193 RVA: 0x0029722C File Offset: 0x0029542C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Exp(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001258 RID: 4696
		private class _RAND_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008593 RID: 34195 RVA: 0x00297277 File Offset: 0x00295477
			public _RAND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x06008594 RID: 34196 RVA: 0x00297284 File Offset: 0x00295484
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip._RAND_Operation._RandomNumberGenerator.NextDouble();
				return index + 1;
			}

			// Token: 0x04007299 RID: 29337
			private static readonly System.Random _RandomNumberGenerator = new System.Random();
		}

		// Token: 0x02001259 RID: 4697
		private class _HCF_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008595 RID: 34197 RVA: 0x002972B9 File Offset: 0x002954B9
			public _HCF_Operation(ProgrammableChip chip, int lineNumber) : base(chip, lineNumber)
			{
			}

			// Token: 0x06008596 RID: 34198 RVA: 0x002972C3 File Offset: 0x002954C3
			public override int Execute(int index)
			{
				this._Chip.HaltAndCatchFire();
				throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ChipCatchingFire, this._LineNumber);
			}
		}

		// Token: 0x0200125A RID: 4698
		private class _YIELD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008597 RID: 34199 RVA: 0x002972DD File Offset: 0x002954DD
			public _YIELD_Operation(ProgrammableChip chip, int lineNumber) : base(chip, lineNumber)
			{
			}

			// Token: 0x06008598 RID: 34200 RVA: 0x002972E7 File Offset: 0x002954E7
			public override int Execute(int index)
			{
				return -index - 1;
			}
		}

		// Token: 0x0200125B RID: 4699
		private class _NOOP_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008599 RID: 34201 RVA: 0x002972ED File Offset: 0x002954ED
			public _NOOP_Operation(ProgrammableChip chip, int lineNUmber) : base(chip, lineNUmber)
			{
			}

			// Token: 0x0600859A RID: 34202 RVA: 0x002972F7 File Offset: 0x002954F7
			public override int Execute(int index)
			{
				return index + 1;
			}
		}

		// Token: 0x0200125C RID: 4700
		private class _POP_Operation : ProgrammableChip._PEEK_Operation
		{
			// Token: 0x0600859B RID: 34203 RVA: 0x002972FC File Offset: 0x002954FC
			public _POP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x0600859C RID: 34204 RVA: 0x00297308 File Offset: 0x00295508
			public override int Execute(int index)
			{
				this._Chip._Registers[this._Chip._StackPointerIndex] -= 1.0;
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int num = (int)Math.Round(this._Chip._Registers[this._Chip._StackPointerIndex]);
				if (num < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
				}
				if (num >= this._Chip._Stack.Length)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = this._Chip._Stack[num];
				CircuitHousing circuitHousing = this._Chip.CircuitHousing as CircuitHousing;
				if (circuitHousing != null)
				{
					LogicLightComponent memoryLight = circuitHousing._MemoryLight;
					if (memoryLight != null)
					{
						memoryLight.Flash(LogicMemoryState.Write);
					}
				}
				return index + 1;
			}
		}

		// Token: 0x0200125D RID: 4701
		private class _PEEK_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x0600859D RID: 34205 RVA: 0x002973DC File Offset: 0x002955DC
			public _PEEK_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x0600859E RID: 34206 RVA: 0x002973E8 File Offset: 0x002955E8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int num = (int)Math.Round(this._Chip._Registers[this._Chip._StackPointerIndex]) - 1;
				if (num < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
				}
				if (num >= this._Chip._Stack.Length)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = this._Chip._Stack[num];
				CircuitHousing circuitHousing = this._Chip.CircuitHousing as CircuitHousing;
				if (circuitHousing != null)
				{
					LogicLightComponent memoryLight = circuitHousing._MemoryLight;
					if (memoryLight != null)
					{
						memoryLight.Flash(LogicMemoryState.Read);
					}
				}
				return index + 1;
			}
		}

		// Token: 0x0200125E RID: 4702
		private class _PUSH_Operation : ProgrammableChip._Operation
		{
			// Token: 0x0600859F RID: 34207 RVA: 0x00297496 File Offset: 0x00295696
			public _PUSH_Operation(ProgrammableChip chip, int lineNumber, string argument1Code) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085A0 RID: 34208 RVA: 0x002974B4 File Offset: 0x002956B4
			public override int Execute(int index)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int num = (int)Math.Round(this._Chip._Registers[this._Chip._StackPointerIndex]);
				if (num < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
				}
				if (num >= this._Chip._Stack.Length)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				this._Chip._Stack[num] = variableValue;
				this._Chip._Registers[this._Chip._StackPointerIndex] += 1.0;
				CircuitHousing circuitHousing = this._Chip.CircuitHousing as CircuitHousing;
				if (circuitHousing != null)
				{
					LogicLightComponent memoryLight = circuitHousing._MemoryLight;
					if (memoryLight != null)
					{
						memoryLight.Flash(LogicMemoryState.Write);
					}
				}
				return index + 1;
			}

			// Token: 0x0400729A RID: 29338
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x0200125F RID: 4703
		private class _CLR_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085A1 RID: 34209 RVA: 0x0029757C File Offset: 0x0029577C
			public _CLR_Operation(ProgrammableChip chip, int lineNumber, string deviceCode) : base(chip, lineNumber)
			{
				this._DeviceIndex = new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
			}

			// Token: 0x060085A2 RID: 34210 RVA: 0x0029759C File Offset: 0x0029579C
			public override int Execute(int index)
			{
				int variableIndex = this._DeviceIndex.GetVariableIndex(ProgrammableChip._AliasTarget.Device, true);
				ILogicable logicableFromIndex = this._Chip.CircuitHousing.GetLogicableFromIndex(variableIndex, int.MinValue);
				if (logicableFromIndex == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryWritable memoryWritable = logicableFromIndex as IMemoryWritable;
				if (memoryWritable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotReadable, this._LineNumber);
				}
				memoryWritable.ClearMemory();
				return index + 1;
			}

			// Token: 0x0400729B RID: 29339
			protected readonly ProgrammableChip._Operation.DeviceIndexVariable _DeviceIndex;
		}

		// Token: 0x02001260 RID: 4704
		private class _CLRD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085A3 RID: 34211 RVA: 0x002975FC File Offset: 0x002957FC
			public _CLRD_Operation(ProgrammableChip chip, int lineNumber, string referenceId) : base(chip, lineNumber)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085A4 RID: 34212 RVA: 0x00297618 File Offset: 0x00295818
			public override int Execute(int index)
			{
				int variableValue = this._DeviceId.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromId = this._Chip.CircuitHousing.GetLogicableFromId(variableValue, int.MinValue);
				if (logicableFromId == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryWritable memoryWritable = logicableFromId as IMemoryWritable;
				if (memoryWritable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotWriteable, this._LineNumber);
				}
				memoryWritable.ClearMemory();
				return index + 1;
			}

			// Token: 0x0400729C RID: 29340
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;
		}

		// Token: 0x02001261 RID: 4705
		private class _GET_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x060085A5 RID: 34213 RVA: 0x00297678 File Offset: 0x00295878
			public _GET_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string deviceCode, string stackIndexCode) : base(chip, lineNumber, registerStoreCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085A6 RID: 34214 RVA: 0x002976A4 File Offset: 0x002958A4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._StackIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryReadable memoryReadable = device as IMemoryReadable;
				if (memoryReadable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotReadable, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = memoryReadable.ReadMemory(variableValue);
				return index + 1;
			}

			// Token: 0x0400729D RID: 29341
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400729E RID: 29342
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001262 RID: 4706
		private class _PUT_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085A7 RID: 34215 RVA: 0x00297722 File Offset: 0x00295922
			public _PUT_Operation(ProgrammableChip chip, int lineNumber, string argument1Code, string deviceCode, string stackIndexCode) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085A8 RID: 34216 RVA: 0x00297760 File Offset: 0x00295960
			public override int Execute(int index)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._StackIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryWritable memoryWritable = device as IMemoryWritable;
				if (memoryWritable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotWriteable, this._LineNumber);
				}
				try
				{
					memoryWritable.WriteMemory(variableValue2, variableValue);
					this._Chip.CircuitHousing.HasPut();
				}
				catch (Exception ex)
				{
					if (ex is NullReferenceException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
					}
					if (ex is StackUnderflowException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
					}
					if (!(ex is StackOverflowException))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.Unknown, this._LineNumber);
					}
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				return index + 1;
			}

			// Token: 0x0400729F RID: 29343
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x040072A0 RID: 29344
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x040072A1 RID: 29345
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001263 RID: 4707
		private class _GETD_Operation : ProgrammableChip._Operation_I
		{
			// Token: 0x060085A9 RID: 34217 RVA: 0x00297848 File Offset: 0x00295A48
			public _GETD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string idCode, string stackIndexCode) : base(chip, lineNumber, registerStoreCode, idCode)
			{
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085AA RID: 34218 RVA: 0x00297868 File Offset: 0x00295A68
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableValue = this._DeviceId.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._StackIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromId = this._Chip.CircuitHousing.GetLogicableFromId(variableValue, int.MinValue);
				if (logicableFromId == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryReadable memoryReadable = logicableFromId as IMemoryReadable;
				if (memoryReadable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotReadable, this._LineNumber);
				}
				this._Chip._Registers[variableIndex] = memoryReadable.ReadMemory(variableValue2);
				return index + 1;
			}

			// Token: 0x040072A2 RID: 29346
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001264 RID: 4708
		private class _PUTD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085AB RID: 34219 RVA: 0x002978F4 File Offset: 0x00295AF4
			public _PUTD_Operation(ProgrammableChip chip, int lineNumber, string argument1Code, string referenceId, string stackIndexCode) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085AC RID: 34220 RVA: 0x00297934 File Offset: 0x00295B34
			public override int Execute(int index)
			{
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._DeviceId.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue3 = this._StackIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromId = this._Chip.CircuitHousing.GetLogicableFromId(variableValue2, int.MinValue);
				if (logicableFromId == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IMemoryWritable memoryWritable = logicableFromId as IMemoryWritable;
				if (memoryWritable == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotWriteable, this._LineNumber);
				}
				try
				{
					memoryWritable.WriteMemory(variableValue3, variableValue);
					this._Chip.CircuitHousing.HasPut();
				}
				catch (Exception ex)
				{
					if (ex is NullReferenceException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
					}
					if (ex is StackUnderflowException)
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
					}
					if (!(ex is StackOverflowException))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.Unknown, this._LineNumber);
					}
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				return index + 1;
			}

			// Token: 0x040072A3 RID: 29347
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x040072A4 RID: 29348
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;

			// Token: 0x040072A5 RID: 29349
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001265 RID: 4709
		private class _POKE_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085AD RID: 34221 RVA: 0x00297A30 File Offset: 0x00295C30
			public _POKE_Operation(ProgrammableChip chip, int lineNumber, string stackIndexCode, string valueCode) : base(chip, lineNumber)
			{
				this._Index = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
				this._Value = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, valueCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085AE RID: 34222 RVA: 0x00297A60 File Offset: 0x00295C60
			public override int Execute(int index)
			{
				double variableValue = this._Value.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				int variableValue2 = this._Index.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue2 < 0)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackUnderFlow, this._LineNumber);
				}
				if (variableValue2 >= this._Chip._Stack.Length)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.StackOverFlow, this._LineNumber);
				}
				this._Chip._Stack[variableValue2] = variableValue;
				return index + 1;
			}

			// Token: 0x040072A6 RID: 29350
			protected ProgrammableChip._Operation.IntValuedVariable _Index;

			// Token: 0x040072A7 RID: 29351
			protected ProgrammableChip._Operation.DoubleValueVariable _Value;
		}

		// Token: 0x02001266 RID: 4710
		private class _SELECT_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x060085AF RID: 34223 RVA: 0x00297ACA File Offset: 0x00295CCA
			public _SELECT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code, string argument3Code) : base(chip, lineNumber, registerStoreCode, argument1Code, argument2Code, argument3Code)
			{
			}

			// Token: 0x060085B0 RID: 34224 RVA: 0x00297ADC File Offset: 0x00295CDC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue != 0.0) ? variableValue2 : variableValue3);
				return index + 1;
			}
		}

		// Token: 0x02001267 RID: 4711
		private class _SLEEP_Operation : ProgrammableChip._Operation
		{
			// Token: 0x060085B1 RID: 34225 RVA: 0x00297B41 File Offset: 0x00295D41
			public _SLEEP_Operation(ProgrammableChip chip, int lineNumber, string sleepDurationCode) : base(chip, lineNumber)
			{
				this._SleepDuration = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, sleepDurationCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x060085B2 RID: 34226 RVA: 0x00297B6C File Offset: 0x00295D6C
			public override int Execute(int index)
			{
				if (double.IsNaN(this.SleepDurationRemaining))
				{
					this.LastTimeSet = GameManager.GameTime;
					this.SleepDurationRemaining = this._SleepDuration.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
					return -index;
				}
				float num = GameManager.GameTime - this.LastTimeSet;
				this.SleepDurationRemaining -= (double)num;
				if (this.SleepDurationRemaining < 0.0)
				{
					this.LastTimeSet = 0f;
					this.SleepDurationRemaining = double.NaN;
					return index + 1;
				}
				this.LastTimeSet = GameManager.GameTime;
				return -index;
			}

			// Token: 0x040072A8 RID: 29352
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _SleepDuration;

			// Token: 0x040072A9 RID: 29353
			public float LastTimeSet;

			// Token: 0x040072AA RID: 29354
			public double SleepDurationRemaining = double.NaN;
		}

		// Token: 0x02001268 RID: 4712
		private class _SRL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060085B3 RID: 34227 RVA: 0x00297BFF File Offset: 0x00295DFF
			public _SRL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060085B4 RID: 34228 RVA: 0x00297C10 File Offset: 0x00295E10
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong >> variableInt);
				return index + 1;
			}
		}

		// Token: 0x02001269 RID: 4713
		private class _SRA_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060085B5 RID: 34229 RVA: 0x00297C63 File Offset: 0x00295E63
			public _SRA_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060085B6 RID: 34230 RVA: 0x00297C74 File Offset: 0x00295E74
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong >> variableInt);
				return index + 1;
			}
		}

		// Token: 0x0200126A RID: 4714
		private class _SLA_SLL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060085B7 RID: 34231 RVA: 0x00297CC7 File Offset: 0x00295EC7
			public _SLA_SLL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060085B8 RID: 34232 RVA: 0x00297CD8 File Offset: 0x00295ED8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				long l = variableLong << variableInt;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x0200126B RID: 4715
		private class _SIN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085B9 RID: 34233 RVA: 0x00297D2B File Offset: 0x00295F2B
			public _SIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085BA RID: 34234 RVA: 0x00297D38 File Offset: 0x00295F38
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Sin(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200126C RID: 4716
		private class _ASIN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085BB RID: 34235 RVA: 0x00297D77 File Offset: 0x00295F77
			public _ASIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085BC RID: 34236 RVA: 0x00297D84 File Offset: 0x00295F84
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Asin(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200126D RID: 4717
		private class _TAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085BD RID: 34237 RVA: 0x00297DC3 File Offset: 0x00295FC3
			public _TAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085BE RID: 34238 RVA: 0x00297DD0 File Offset: 0x00295FD0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Tan(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200126E RID: 4718
		private class _ATAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085BF RID: 34239 RVA: 0x00297E0F File Offset: 0x0029600F
			public _ATAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085C0 RID: 34240 RVA: 0x00297E1C File Offset: 0x0029601C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Atan(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200126F RID: 4719
		private class _ATAN2_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x060085C1 RID: 34241 RVA: 0x00297E5B File Offset: 0x0029605B
			public _ATAN2_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x060085C2 RID: 34242 RVA: 0x00297E6C File Offset: 0x0029606C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Atan2(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x02001270 RID: 4720
		private class _COS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085C3 RID: 34243 RVA: 0x00297EBA File Offset: 0x002960BA
			public _COS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085C4 RID: 34244 RVA: 0x00297EC8 File Offset: 0x002960C8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Cos(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001271 RID: 4721
		private class _ACOS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085C5 RID: 34245 RVA: 0x00297F07 File Offset: 0x00296107
			public _ACOS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x060085C6 RID: 34246 RVA: 0x00297F14 File Offset: 0x00296114
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Acos(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001272 RID: 4722
		private class _BDNVS_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x060085C7 RID: 34247 RVA: 0x00297F53 File Offset: 0x00296153
			public _BDNVS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string argument1Code) : base(chip, lineNumber, argument1Code)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060085C8 RID: 34248 RVA: 0x00297F84 File Offset: 0x00296184
			public override int Execute(int index)
			{
				LogicType variableValue = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				if (!device.CanLogicWrite(variableValue))
				{
					return this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				return index + 1;
			}

			// Token: 0x040072AB RID: 29355
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x040072AC RID: 29356
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001273 RID: 4723
		private class _BDNVL_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x060085C9 RID: 34249 RVA: 0x00297FF1 File Offset: 0x002961F1
			public _BDNVL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string argument1Code) : base(chip, lineNumber, argument1Code)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x060085CA RID: 34250 RVA: 0x00298020 File Offset: 0x00296220
			public override int Execute(int index)
			{
				LogicType variableValue = this._LogicType.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				if (variableValue == LogicType.None)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.LogicTypeIsNone, this._LineNumber);
				}
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				if (!device.CanLogicRead(variableValue))
				{
					return this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				}
				return index + 1;
			}

			// Token: 0x040072AD RID: 29357
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x040072AE RID: 29358
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001274 RID: 4724
		private class _RMAP_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x060085CB RID: 34251 RVA: 0x0029808D File Offset: 0x0029628D
			public _RMAP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string deviceCode, string argument1Code) : base(chip, lineNumber, registerStoreCode, argument1Code)
			{
				this._DeviceIndex = new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
			}

			// Token: 0x060085CC RID: 34252 RVA: 0x002980B0 File Offset: 0x002962B0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				int variableIndex2 = this._DeviceIndex.GetVariableIndex(ProgrammableChip._AliasTarget.Device, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				ILogicable logicableFromIndex = this._Chip.CircuitHousing.GetLogicableFromIndex(variableIndex2, int.MinValue);
				if (logicableFromIndex == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotFound, this._LineNumber);
				}
				IRequireReagent requireReagent = logicableFromIndex as IRequireReagent;
				if (requireReagent == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.MemoryNotReadable, this._LineNumber);
				}
				int prefabHashFromReagentHash = requireReagent.GetPrefabHashFromReagentHash((int)variableValue);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble((long)prefabHashFromReagentHash);
				return index + 1;
			}

			// Token: 0x040072AF RID: 29359
			protected readonly ProgrammableChip._Operation.DeviceIndexVariable _DeviceIndex;
		}

		// Token: 0x02001275 RID: 4725
		public class ScriptEnum<T> : IScriptEnum where T : struct, Enum, IConvertible
		{
			// Token: 0x060085CD RID: 34253 RVA: 0x00298144 File Offset: 0x00296344
			public ScriptEnum(InstructionInclude logicType, Func<T, bool> isDeprecated, Func<T, string> getDescription = null)
			{
				this._includeType = logicType;
				this._types = (T[])Enum.GetValues(typeof(T));
				this._names = Enum.GetNames(typeof(T));
				this._isDeprecated = isDeprecated;
				this._color = "orange";
				this._getDescription = getDescription;
				this._typeHash = Animator.StringToHash(typeof(T).Name);
			}

			// Token: 0x060085CE RID: 34254 RVA: 0x002981C0 File Offset: 0x002963C0
			public int Count()
			{
				return this._types.Length;
			}

			// Token: 0x060085CF RID: 34255 RVA: 0x002981CA File Offset: 0x002963CA
			public bool IsDeprecated(int i)
			{
				Func<T, bool> isDeprecated = this._isDeprecated;
				return isDeprecated != null && isDeprecated(this._types[i]);
			}

			// Token: 0x060085D0 RID: 34256 RVA: 0x002981EC File Offset: 0x002963EC
			public HelpReference MakePage(int i, HelpReference prefab, RectTransform parent)
			{
				T t = this._types[i];
				Func<T, bool> isDeprecated = this._isDeprecated;
				if (isDeprecated != null && isDeprecated(t))
				{
					return null;
				}
				HelpReference helpReference = UnityEngine.Object.Instantiate<HelpReference>(prefab, parent);
				int num = Convert.ToInt32(t);
				helpReference.Text.text = string.Concat(new string[]
				{
					"<color=",
					this._color,
					">",
					this._names[i],
					"</color>"
				});
				helpReference.Text2.text = "<color=#808080>" + typeof(T).Name + "</color>";
				helpReference.ReferenceValue1 = Animator.StringToHash(this._names[i]);
				helpReference.ReferenceValue2 = Animator.StringToHash(typeof(T).Name);
				Func<T, string> getDescription = this._getDescription;
				string text = ((getDescription != null) ? getDescription(t) : null) ?? string.Empty;
				helpReference.TextString = this._names[i];
				helpReference.TypeString = typeof(T).Name;
				helpReference.DescString = text;
				text = "<color=yellow>" + num.ToString("0." + new string('#', 339), CultureInfo.CurrentCulture) + "</color><br>" + text;
				if (string.IsNullOrEmpty(text))
				{
					helpReference.Description.gameObject.SetActive(false);
				}
				else
				{
					helpReference.Description.text = text;
				}
				return helpReference;
			}

			// Token: 0x060085D1 RID: 34257 RVA: 0x0029836C File Offset: 0x0029656C
			public bool TryParse(string searchText)
			{
				T t;
				return Enum.TryParse<T>(searchText, out t);
			}

			// Token: 0x060085D2 RID: 34258 RVA: 0x00298381 File Offset: 0x00296581
			public bool IsHashType(int hash)
			{
				return this._typeHash == hash;
			}

			// Token: 0x060085D3 RID: 34259 RVA: 0x0029838C File Offset: 0x0029658C
			public void Parse(ref string masterString)
			{
				for (int i = 0; i < this._types.Length; i++)
				{
					string text = this._names[i];
					Func<T, bool> isDeprecated = this._isDeprecated;
					if (isDeprecated != null && isDeprecated(this._types[i]))
					{
						text = "<s>" + text + "</s>";
					}
					masterString = masterString.ReplaceWholeWord(this._names[i], string.Format("<color={1}>{0}</color>", text, this._color), null);
				}
			}

			// Token: 0x060085D4 RID: 34260 RVA: 0x00298409 File Offset: 0x00296609
			public void Execute(ref bool isValueSet, ref double value, string code, InstructionInclude propertiesToUse)
			{
				if (isValueSet || (propertiesToUse & this._includeType) == InstructionInclude.None)
				{
					return;
				}
				if (!Enum.IsDefined(typeof(T), code))
				{
					return;
				}
				value = (double)Convert.ToInt32(Enum.Parse(typeof(T), code));
				isValueSet = true;
			}

			// Token: 0x060085D5 RID: 34261 RVA: 0x00298449 File Offset: 0x00296649
			public void Execute(ref bool isValueSet, ref int value, string code, InstructionInclude propertiesToUse)
			{
				if (isValueSet || (propertiesToUse & this._includeType) == InstructionInclude.None)
				{
					return;
				}
				if (!Enum.IsDefined(typeof(T), code))
				{
					return;
				}
				value = Convert.ToInt32(Enum.Parse(typeof(T), code));
				isValueSet = true;
			}

			// Token: 0x040072B0 RID: 29360
			private readonly InstructionInclude _includeType;

			// Token: 0x040072B1 RID: 29361
			private readonly T[] _types;

			// Token: 0x040072B2 RID: 29362
			private readonly int _typeHash;

			// Token: 0x040072B3 RID: 29363
			private readonly string[] _names;

			// Token: 0x040072B4 RID: 29364
			private readonly Func<T, bool> _isDeprecated;

			// Token: 0x040072B5 RID: 29365
			private readonly Func<T, string> _getDescription;

			// Token: 0x040072B6 RID: 29366
			private readonly string _color;
		}

		// Token: 0x02001276 RID: 4726
		public class BasicEnum<T> : IScriptEnum where T : struct, Enum, IConvertible
		{
			// Token: 0x060085D6 RID: 34262 RVA: 0x00298488 File Offset: 0x00296688
			public BasicEnum(string typeString = "", Func<T, bool> isDeprecated = null)
			{
				this._types = (T[])Enum.GetValues(typeof(T));
				this._names = Enum.GetNames(typeof(T));
				this._color = "#20B2AA";
				this._typeString = typeString;
				if (!string.IsNullOrEmpty(typeString))
				{
					for (int i = 0; i < this._names.Length; i++)
					{
						this._names[i] = this._typeString + "." + this._names[i];
					}
				}
				this._typeHash = Animator.StringToHash(typeString);
				this._isDeprecated = isDeprecated;
			}

			// Token: 0x060085D7 RID: 34263 RVA: 0x0029852C File Offset: 0x0029672C
			public HelpReference MakePage(int i, HelpReference prefab, RectTransform parent)
			{
				T t = this._types[i];
				Func<T, bool> isDeprecated = this._isDeprecated;
				if (isDeprecated != null && isDeprecated(t))
				{
					return null;
				}
				HelpReference helpReference = UnityEngine.Object.Instantiate<HelpReference>(prefab, parent);
				int num = Convert.ToInt32(t);
				helpReference.Text.text = string.Concat(new string[]
				{
					"<color=",
					this._color,
					">",
					this._names[i],
					"</color>"
				});
				helpReference.Text2.text = "<color=#808080>Constant</color>";
				helpReference.ReferenceValue1 = Animator.StringToHash(this._names[i]);
				helpReference.ReferenceValue2 = Animator.StringToHash(this._typeString);
				helpReference.DescString = string.Empty;
				helpReference.Description.text = "<color=yellow>" + num.ToString("0." + new string('#', 339), CultureInfo.CurrentCulture) + "</color><br>";
				helpReference.TextString = this._names[i];
				helpReference.TypeString = this._typeString;
				return helpReference;
			}

			// Token: 0x060085D8 RID: 34264 RVA: 0x00298649 File Offset: 0x00296849
			public int Count()
			{
				return this._types.Length;
			}

			// Token: 0x060085D9 RID: 34265 RVA: 0x00298653 File Offset: 0x00296853
			public bool IsDeprecated(int i)
			{
				Func<T, bool> isDeprecated = this._isDeprecated;
				return isDeprecated != null && isDeprecated(this._types[i]);
			}

			// Token: 0x060085DA RID: 34266 RVA: 0x00298674 File Offset: 0x00296874
			public bool TryParse(string searchText)
			{
				T t;
				return Enum.TryParse<T>(searchText, out t);
			}

			// Token: 0x060085DB RID: 34267 RVA: 0x00298689 File Offset: 0x00296889
			public bool IsHashType(int hash)
			{
				return this._typeHash == hash;
			}

			// Token: 0x060085DC RID: 34268 RVA: 0x00298694 File Offset: 0x00296894
			public void Parse(ref string masterString)
			{
				for (int i = 0; i < this._types.Length; i++)
				{
					string text = this._names[i];
					Func<T, bool> isDeprecated = this._isDeprecated;
					if (isDeprecated != null && isDeprecated(this._types[i]))
					{
						text = "<s>" + text + "</s>";
					}
					masterString = masterString.ReplaceWholeWord(text, string.Format("<color={1}>{0}</color>", text, this._color), null);
				}
			}

			// Token: 0x060085DD RID: 34269 RVA: 0x0029870C File Offset: 0x0029690C
			public void Execute(ref bool isValueSet, ref double value, string code, InstructionInclude propertiesToUse)
			{
				if (isValueSet || (propertiesToUse & InstructionInclude.Enum) == InstructionInclude.None)
				{
					return;
				}
				if (!string.IsNullOrEmpty(this._typeString))
				{
					string[] array = code.Split('.', StringSplitOptions.None);
					if (array.Length != 2)
					{
						return;
					}
					if (!array[0].Equals(this._typeString, StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
					code = array[1];
				}
				if (!Enum.IsDefined(typeof(T), code))
				{
					return;
				}
				value = (double)Convert.ToInt32(Enum.Parse(typeof(T), code));
				isValueSet = true;
			}

			// Token: 0x060085DE RID: 34270 RVA: 0x00298788 File Offset: 0x00296988
			public void Execute(ref bool isValueSet, ref int value, string code, InstructionInclude propertiesToUse)
			{
				if (isValueSet || (propertiesToUse & InstructionInclude.Enum) == InstructionInclude.None)
				{
					return;
				}
				if (!string.IsNullOrEmpty(this._typeString))
				{
					string[] array = code.Split('.', StringSplitOptions.None);
					if (array.Length != 2)
					{
						return;
					}
					if (!array[0].Equals(this._typeString, StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
					code = array[1];
				}
				if (!Enum.IsDefined(typeof(T), code))
				{
					return;
				}
				value = Convert.ToInt32(Enum.Parse(typeof(T), code));
				isValueSet = true;
			}

			// Token: 0x040072B7 RID: 29367
			private readonly T[] _types;

			// Token: 0x040072B8 RID: 29368
			private readonly string[] _names;

			// Token: 0x040072B9 RID: 29369
			private readonly string _color;

			// Token: 0x040072BA RID: 29370
			private readonly string _typeString;

			// Token: 0x040072BB RID: 29371
			private readonly int _typeHash;

			// Token: 0x040072BC RID: 29372
			private readonly Func<T, bool> _isDeprecated;
		}
	}
}
