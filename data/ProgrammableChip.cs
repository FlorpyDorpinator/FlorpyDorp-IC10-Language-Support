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
	// Token: 0x02000D25 RID: 3365
	public class ProgrammableChip : Item, ISourceCode, IReferencable, IEvaluable, ISpatial, IPhysical, IProfile, IDensePoolable, ILogicable, IMemoryWritable, IMemory, IMemoryReadable
	{
		// Token: 0x060075C8 RID: 30152 RVA: 0x002388CF File Offset: 0x00236ACF
		public override string GetStationpediaCategory()
		{
			return Localization.GetInterface(StationpediaCategoryStrings.LogicIntegratedCircuitsCategory, false);
		}

		// Token: 0x060075C9 RID: 30153 RVA: 0x002388DC File Offset: 0x00236ADC
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

		// Token: 0x060075CA RID: 30154 RVA: 0x00238938 File Offset: 0x00236B38
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

		// Token: 0x060075CB RID: 30155 RVA: 0x0023899E File Offset: 0x00236B9E
		public override bool CanLogicRead(LogicType logicType)
		{
			return logicType == LogicType.LineNumber || base.CanLogicRead(logicType);
		}

		// Token: 0x060075CC RID: 30156 RVA: 0x002389B1 File Offset: 0x00236BB1
		public override double GetLogicValue(LogicType logicType)
		{
			if (logicType == LogicType.LineNumber)
			{
				return (double)this._NextAddr;
			}
			return base.GetLogicValue(logicType);
		}

		// Token: 0x1700159C RID: 5532
		// (get) Token: 0x060075CE RID: 30158 RVA: 0x00238EA2 File Offset: 0x002370A2
		// (set) Token: 0x060075CF RID: 30159 RVA: 0x00238EAA File Offset: 0x002370AA
		public char[] SourceCodeCharArray { get; set; }

		// Token: 0x1700159D RID: 5533
		// (get) Token: 0x060075D0 RID: 30160 RVA: 0x00238EB3 File Offset: 0x002370B3
		// (set) Token: 0x060075D1 RID: 30161 RVA: 0x00238EBB File Offset: 0x002370BB
		public int SourceCodeWritePointer { get; set; }

		// Token: 0x1700159E RID: 5534
		// (get) Token: 0x060075D2 RID: 30162 RVA: 0x00238EC4 File Offset: 0x002370C4
		public string ErrorLineNumberString
		{
			get
			{
				return StringManager.Get((int)this._ErrorLineNumberSynced);
			}
		}

		// Token: 0x1700159F RID: 5535
		// (get) Token: 0x060075D3 RID: 30163 RVA: 0x00238ED1 File Offset: 0x002370D1
		// (set) Token: 0x060075D4 RID: 30164 RVA: 0x00238ED9 File Offset: 0x002370D9
		public ulong LastEditedId { get; set; }

		// Token: 0x170015A0 RID: 5536
		// (get) Token: 0x060075D5 RID: 30165 RVA: 0x00238EE2 File Offset: 0x002370E2
		// (set) Token: 0x060075D6 RID: 30166 RVA: 0x00238EEA File Offset: 0x002370EA
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

		// Token: 0x170015A1 RID: 5537
		// (get) Token: 0x060075D7 RID: 30167 RVA: 0x00238F16 File Offset: 0x00237116
		public string ErrorTypeString
		{
			get
			{
				return ProgrammableChip._exceptionTypes.GetName(this._ErrorType, false);
			}
		}

		// Token: 0x170015A2 RID: 5538
		// (get) Token: 0x060075D8 RID: 30168 RVA: 0x00238F29 File Offset: 0x00237129
		// (set) Token: 0x060075D9 RID: 30169 RVA: 0x00238F34 File Offset: 0x00237134
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

		// Token: 0x170015A3 RID: 5539
		// (get) Token: 0x060075DA RID: 30170 RVA: 0x00238F6D File Offset: 0x0023716D
		public bool CompilationError
		{
			get
			{
				return this.CompileErrorType != ProgrammableChipException.ICExceptionType.None;
			}
		}

		// Token: 0x170015A4 RID: 5540
		// (get) Token: 0x060075DB RID: 30171 RVA: 0x00238F7B File Offset: 0x0023717B
		// (set) Token: 0x060075DC RID: 30172 RVA: 0x00238F83 File Offset: 0x00237183
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

		// Token: 0x170015A5 RID: 5541
		// (get) Token: 0x060075DD RID: 30173 RVA: 0x00238FA6 File Offset: 0x002371A6
		// (set) Token: 0x060075DE RID: 30174 RVA: 0x00238FAE File Offset: 0x002371AE
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

		// Token: 0x170015A6 RID: 5542
		// (get) Token: 0x060075DF RID: 30175 RVA: 0x00238FD1 File Offset: 0x002371D1
		private ICircuitHolder CircuitHousing
		{
			get
			{
				Slot parentSlot = base.ParentSlot;
				return ((parentSlot != null) ? parentSlot.Parent : null) as ICircuitHolder;
			}
		}

		// Token: 0x060075E0 RID: 30176 RVA: 0x00238FEC File Offset: 0x002371EC
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

		// Token: 0x060075E1 RID: 30177 RVA: 0x00239058 File Offset: 0x00237258
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
				this.SetSourceCode(reader.ReadAscii());
			}
		}

		// Token: 0x060075E2 RID: 30178 RVA: 0x002390C8 File Offset: 0x002372C8
		public override void SerializeOnJoin(RocketBinaryWriter writer)
		{
			base.SerializeOnJoin(writer);
			writer.WriteUInt16(this._ErrorLineNumber);
			writer.WriteByte((byte)this._ErrorType);
			writer.WriteUInt16(this.CompileErrorLineNumber);
			writer.WriteByte((byte)this.CompileErrorType);
			writer.WriteAscii(this.SourceCode);
		}

		// Token: 0x060075E3 RID: 30179 RVA: 0x00239118 File Offset: 0x00237318
		public override void DeserializeOnJoin(RocketBinaryReader reader)
		{
			base.DeserializeOnJoin(reader);
			this._ErrorLineNumber = reader.ReadUInt16();
			this._ErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
			this.CompileErrorLineNumber = reader.ReadUInt16();
			this.CompileErrorType = (ProgrammableChipException.ICExceptionType)reader.ReadByte();
			this.SetSourceCode(reader.ReadAscii());
		}

		// Token: 0x060075E4 RID: 30180 RVA: 0x0023916D File Offset: 0x0023736D
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

		// Token: 0x170015A7 RID: 5543
		// (get) Token: 0x060075E5 RID: 30181 RVA: 0x002391A2 File Offset: 0x002373A2
		public float MemoryUsed
		{
			get
			{
				return (float)this.SourceCode.Length;
			}
		}

		// Token: 0x060075E6 RID: 30182 RVA: 0x002391B0 File Offset: 0x002373B0
		public override string GetQuantityText()
		{
			return StringGenerator.GetString((int)this.MemoryUsed, Unit.ProgrammableChip);
		}

		// Token: 0x060075E7 RID: 30183 RVA: 0x002391BF File Offset: 0x002373BF
		public void SetSourceCode(string sourceCode, ICircuitHolder parent)
		{
			parent.ClearError();
			this.SetSourceCode(sourceCode);
		}

		// Token: 0x060075E8 RID: 30184 RVA: 0x002391D0 File Offset: 0x002373D0
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
			Slot parentSlot = base.ParentSlot;
			if (parentSlot == null)
			{
				return;
			}
			parentSlot.RefreshQuantity();
		}

		// Token: 0x060075E9 RID: 30185 RVA: 0x002393C4 File Offset: 0x002375C4
		public AsciiString GetSourceCode()
		{
			return this.SourceCode;
		}

		// Token: 0x060075EA RID: 30186 RVA: 0x002393CC File Offset: 0x002375CC
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

		// Token: 0x060075EB RID: 30187 RVA: 0x00239444 File Offset: 0x00237644
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

		// Token: 0x060075EC RID: 30188 RVA: 0x0023959C File Offset: 0x0023779C
		public override ThingSaveData SerializeSave()
		{
			ThingSaveData thingSaveData;
			ThingSaveData result = thingSaveData = new ProgrammableChipSaveData();
			this.InitialiseSaveData(ref thingSaveData);
			return result;
		}

		// Token: 0x060075ED RID: 30189 RVA: 0x002395B8 File Offset: 0x002377B8
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

		// Token: 0x060075EE RID: 30190 RVA: 0x00239820 File Offset: 0x00237A20
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

		// Token: 0x060075EF RID: 30191 RVA: 0x00239B44 File Offset: 0x00237D44
		public static string GetIntroString()
		{
			return string.Format("These functions are generally typed per line of instruction to your Integrated Circuit (IC). Below are a list of the functions. {0} refers to a device, the ? character replaced with either the screw number, or 'b' for base unit. For example 'd0' or 'db'. {1} refers to a register, the ? refers to the number of the register, such as 'r0'. Additional r in front allows indirect referencing. {2} refers to a logic variable, whether slot or general.", "<color=green>d?</color>", "<color=#0080FFFF>r?</color>", "<color=orange>var</color>");
		}

		// Token: 0x060075F0 RID: 30192 RVA: 0x00239B60 File Offset: 0x00237D60
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

		// Token: 0x060075F1 RID: 30193 RVA: 0x00239C0C File Offset: 0x00237E0C
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

		// Token: 0x060075F2 RID: 30194 RVA: 0x00239CAA File Offset: 0x00237EAA
		public static string SetString(string format, ScriptCommand command)
		{
			return ProgrammableChip.SetString(format, command.ToString());
		}

		// Token: 0x060075F3 RID: 30195 RVA: 0x00239CC0 File Offset: 0x00237EC0
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
			case ScriptCommand.sgn:
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
			case ScriptCommand.rol:
			case ScriptCommand.ror:
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
			case ScriptCommand.clamp:
				return ProgrammableChip.MakeString(command, color, spaceCount, new ProgrammableChip.HelpString[]
				{
					ProgrammableChip.REGISTER,
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("a"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("min"),
					(ProgrammableChip.REGISTER + ProgrammableChip.NUMBER).Var("max")
				});
			default:
				throw new ArgumentOutOfRangeException(Localization.GetInterface("ScriptCommandCommand", false), command, null);
			}
		}

		// Token: 0x060075F4 RID: 30196 RVA: 0x0023ABF8 File Offset: 0x00238DF8
		private UniTaskVoid HaltAndCatchFireFromThread()
		{
			ProgrammableChip.<HaltAndCatchFireFromThread>d__125 <HaltAndCatchFireFromThread>d__;
			<HaltAndCatchFireFromThread>d__.<>t__builder = AsyncUniTaskVoidMethodBuilder.Create();
			<HaltAndCatchFireFromThread>d__.<>4__this = this;
			<HaltAndCatchFireFromThread>d__.<>1__state = -1;
			<HaltAndCatchFireFromThread>d__.<>t__builder.Start<ProgrammableChip.<HaltAndCatchFireFromThread>d__125>(ref <HaltAndCatchFireFromThread>d__);
			return <HaltAndCatchFireFromThread>d__.<>t__builder.Task;
		}

		// Token: 0x060075F5 RID: 30197 RVA: 0x0023AC3C File Offset: 0x00238E3C
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

		// Token: 0x060075F6 RID: 30198 RVA: 0x0023AC83 File Offset: 0x00238E83
		public void Reset()
		{
			this._NextAddr = 0;
			this._Registers[this._StackPointerIndex] = 0.0;
			this._ErrorType = ProgrammableChipException.ICExceptionType.None;
			this._ErrorLineNumber = 0;
		}

		// Token: 0x060075F7 RID: 30199 RVA: 0x0023ACB0 File Offset: 0x00238EB0
		public override PassiveTooltip GetPassiveTooltip(Collider hitCollider)
		{
			PassiveTooltip passiveTooltip = base.GetPassiveTooltip(hitCollider);
			passiveTooltip.Extended += this.GetErrorCode();
			return passiveTooltip;
		}

		// Token: 0x060075F8 RID: 30200 RVA: 0x0023ACDB File Offset: 0x00238EDB
		public override StringBuilder GetExtendedText()
		{
			StringBuilder extendedText = base.GetExtendedText();
			extendedText.AppendLine(GameStrings.ItemInSlotValue.AsString(base.ToTooltip(), this.GetQuantityText()));
			extendedText.Append(this.GetErrorCode());
			return extendedText;
		}

		// Token: 0x060075F9 RID: 30201 RVA: 0x0023AD10 File Offset: 0x00238F10
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

		// Token: 0x060075FA RID: 30202 RVA: 0x0023AD94 File Offset: 0x00238F94
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

		// Token: 0x060075FB RID: 30203 RVA: 0x0023ADF0 File Offset: 0x00238FF0
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

		// Token: 0x060075FC RID: 30204 RVA: 0x0023AE20 File Offset: 0x00239020
		public static long DoubleToLong(double d, bool signed)
		{
			long num = (long)(d % 9007199254740992.0);
			if (!signed)
			{
				num &= 18014398509481983L;
			}
			return num;
		}

		// Token: 0x170015A8 RID: 5544
		// (get) Token: 0x060075FD RID: 30205 RVA: 0x0023AE4A File Offset: 0x0023904A
		// (set) Token: 0x060075FE RID: 30206 RVA: 0x0023AE54 File Offset: 0x00239054
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

		// Token: 0x060075FF RID: 30207 RVA: 0x0023AE98 File Offset: 0x00239098
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

		// Token: 0x06007600 RID: 30208 RVA: 0x0023AEBD File Offset: 0x002390BD
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

		// Token: 0x06007601 RID: 30209 RVA: 0x0023AEE4 File Offset: 0x002390E4
		public void ClearMemory()
		{
			for (int i = 0; i < this._Stack.Length; i++)
			{
				this._Stack[i] = 0.0;
			}
		}

		// Token: 0x06007602 RID: 30210 RVA: 0x0023AF15 File Offset: 0x00239115
		public int GetStackSize()
		{
			return this._Stack.Length;
		}

		// Token: 0x06007603 RID: 30211 RVA: 0x0023AF1F File Offset: 0x0023911F
		public static string StripColorTags(string input)
		{
			return Regex.Replace(input, "<color\\s*=\\s*['\"]?(?<color>[^'\">]+)['\"]?\\s*>(?<content>.*?)<\\/color>", "${content}");
		}

		// Token: 0x06007604 RID: 30212 RVA: 0x0023AF34 File Offset: 0x00239134
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
			case ScriptCommand.sgn:
				return GameStrings.ScriptDescriptionSgn.DisplayString;
			case ScriptCommand.clamp:
				return GameStrings.ScriptDescriptionClamp.DisplayString;
			case ScriptCommand.rol:
				return GameStrings.ScriptDescriptionRol.DisplayString;
			case ScriptCommand.ror:
				return GameStrings.ScriptDescriptionRor.DisplayString;
			default:
				throw new ArgumentOutOfRangeException(Localization.GetInterface("ScriptCommandCommand", false), command, null);
			}
		}

		// Token: 0x04005902 RID: 22786
		[SerializeField]
		[ReadOnly]
		private readonly double[] _Registers = new double[18];

		// Token: 0x04005903 RID: 22787
		private readonly int _StackPointerIndex = 16;

		// Token: 0x04005904 RID: 22788
		private readonly int _ReturnAddressIndex = 17;

		// Token: 0x04005905 RID: 22789
		private readonly double[] _Stack = new double[512];

		// Token: 0x04005906 RID: 22790
		public AsciiString SourceCode = AsciiString.Empty;

		// Token: 0x04005909 RID: 22793
		private readonly Dictionary<string, ProgrammableChip._AliasValue> _Aliases = new Dictionary<string, ProgrammableChip._AliasValue>();

		// Token: 0x0400590A RID: 22794
		private readonly Dictionary<string, int> _JumpTags = new Dictionary<string, int>();

		// Token: 0x0400590B RID: 22795
		private readonly Dictionary<string, double> _Defines = new Dictionary<string, double>();

		// Token: 0x0400590C RID: 22796
		[ByteArraySync]
		private ushort _ErrorLineNumberSynced;

		// Token: 0x0400590E RID: 22798
		private static EnumCollection<ProgrammableChipException.ICExceptionType, byte> _exceptionTypes = new EnumCollection<ProgrammableChipException.ICExceptionType, byte>(false);

		// Token: 0x0400590F RID: 22799
		[ByteArraySync]
		private byte _ErrorTypeSynced;

		// Token: 0x04005910 RID: 22800
		private ushort _compileErrorLineNumber;

		// Token: 0x04005911 RID: 22801
		private ProgrammableChipException.ICExceptionType _compileErrorType;

		// Token: 0x04005912 RID: 22802
		private int _NextAddr;

		// Token: 0x04005913 RID: 22803
		private readonly List<ProgrammableChip._LineOfCode> _LinesOfCode = new List<ProgrammableChip._LineOfCode>();

		// Token: 0x04005914 RID: 22804
		private int _executeIndex;

		// Token: 0x04005915 RID: 22805
		public const string _strCommand = "<color=yellow>{0}</color>";

		// Token: 0x04005916 RID: 22806
		public const string _strDevice = "<color=green>d?</color>";

		// Token: 0x04005917 RID: 22807
		public const string _strLogicType = "<color=orange>var</color>";

		// Token: 0x04005918 RID: 22808
		public const string _strNumber = "<color=white>num</color>";

		// Token: 0x04005919 RID: 22809
		public const string _strInteger = "<color=white>int</color>";

		// Token: 0x0400591A RID: 22810
		public const string _strOr = "<color=#585858FF>|</color>";

		// Token: 0x0400591B RID: 22811
		public const string _strRegister = "<color=#0080FFFF>r?</color>";

		// Token: 0x0400591C RID: 22812
		public const string _strString = "<color=white>str</color>";

		// Token: 0x0400591D RID: 22813
		public const string _strAny = "<color=#0080FFFF>r?</color>|<color=white>num</color>";

		// Token: 0x0400591E RID: 22814
		public const string _strRegOrDev = "<color=#0080FFFF>r?</color>|<color=green>d?</color>";

		// Token: 0x0400591F RID: 22815
		public const string _strReagentMode = "<color=orange>reagentMode</color>";

		// Token: 0x04005920 RID: 22816
		public const string _strReagent = "<color=white>reagent</color>";

		// Token: 0x04005921 RID: 22817
		public const string _strType = "<color=white>type</color>";

		// Token: 0x04005922 RID: 22818
		public const string _strBatchMode = "<color=orange>batchMode</color>";

		// Token: 0x04005923 RID: 22819
		public const string _strValues = "<color=lightblue>...</color>";

		// Token: 0x04005924 RID: 22820
		private const string FORMAT_VARIABLE = "<color=orange>{0}</color>";

		// Token: 0x04005925 RID: 22821
		private const string FORMAT_NUMBER = "<color=lightblue>{0}</color>";

		// Token: 0x04005926 RID: 22822
		private const string FORMAT_TEXT = "<color=white>{0}</color>";

		// Token: 0x04005927 RID: 22823
		private static readonly ProgrammableChip.HelpString STRING = new ProgrammableChip.HelpString("str", "white");

		// Token: 0x04005928 RID: 22824
		private static readonly ProgrammableChip.HelpString DEVICE_INDEX = new ProgrammableChip.HelpString("d?", "green");

		// Token: 0x04005929 RID: 22825
		private static readonly ProgrammableChip.HelpString REGISTER = new ProgrammableChip.HelpString("r?", "#0080FFFF");

		// Token: 0x0400592A RID: 22826
		private static readonly ProgrammableChip.HelpString INTEGER = new ProgrammableChip.HelpString("int", "#20B2AA");

		// Token: 0x0400592B RID: 22827
		private static readonly ProgrammableChip.HelpString NUMBER = new ProgrammableChip.HelpString("num", "#20B2AA");

		// Token: 0x0400592C RID: 22828
		private static readonly ProgrammableChip.HelpString REF_ID = new ProgrammableChip.HelpString("id", "#20B2AA");

		// Token: 0x0400592D RID: 22829
		private static readonly ProgrammableChip.HelpString OR = new ProgrammableChip.HelpString("or", "|", "#585858FF");

		// Token: 0x0400592E RID: 22830
		private static readonly ProgrammableChip.HelpString LOGIC_TYPE = new ProgrammableChip.HelpString("logicType", "orange");

		// Token: 0x0400592F RID: 22831
		private static readonly ProgrammableChip.HelpString LOGIC_SLOT_TYPE = new ProgrammableChip.HelpString("logicSlotType", "orange");

		// Token: 0x04005930 RID: 22832
		private static readonly ProgrammableChip.HelpString BATCH_MODE = new ProgrammableChip.HelpString("batchMode", "orange");

		// Token: 0x04005931 RID: 22833
		private static readonly ProgrammableChip.HelpString DEVICE_HASH = new ProgrammableChip.HelpString("deviceHash", "#20B2AA");

		// Token: 0x04005932 RID: 22834
		private static readonly ProgrammableChip.HelpString NAME_HASH = new ProgrammableChip.HelpString("nameHash", "#20B2AA");

		// Token: 0x04005933 RID: 22835
		private static readonly ProgrammableChip.HelpString SLOT_INDEX = new ProgrammableChip.HelpString("slotIndex", "#20B2AA");

		// Token: 0x04005934 RID: 22836
		private static readonly ProgrammableChip.HelpString REAGENT_MODE = new ProgrammableChip.HelpString("reagentMode", "orange");

		// Token: 0x04005935 RID: 22837
		public const char REGISTER_CHAR = 'r';

		// Token: 0x04005936 RID: 22838
		public const char DEVICE_CHAR = 'd';

		// Token: 0x04005937 RID: 22839
		public const string BASE_UNIT_STRING = "db";

		// Token: 0x04005938 RID: 22840
		public const int BASE_UNIT_INDEX = 2147483647;

		// Token: 0x04005939 RID: 22841
		public const int BASE_NETWORK_INDEX = -2147483648;

		// Token: 0x0400593A RID: 22842
		public const int FIRST_AVAILABLE_NETWORK = 2147483647;

		// Token: 0x0400593B RID: 22843
		public const string RETURN_ADDRESS_STRING = "ra";

		// Token: 0x0400593C RID: 22844
		public const string STACK_POINTER_STRING = "sp";

		// Token: 0x0400593D RID: 22845
		public const char HEX_CHAR = '$';

		// Token: 0x0400593E RID: 22846
		public const char BINARY_CHAR = '%';

		// Token: 0x0400593F RID: 22847
		public const char COMMENT_CHAR = '#';

		// Token: 0x04005940 RID: 22848
		public const char NETWORK_CHAR = ':';

		// Token: 0x04005941 RID: 22849
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

		// Token: 0x04005942 RID: 22850
		private const int _RotateWidth = 54;

		// Token: 0x04005943 RID: 22851
		private const long _RotateMask = 18014398509481983L;

		// Token: 0x04005944 RID: 22852
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
			new ProgrammableChip.BasicEnum<FiltrationMode>("FiltrationMode", null),
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
			new ProgrammableChip.BasicEnum<SettingDisplayMode>("SettingDisplayMode", null)
		};

		// Token: 0x02001276 RID: 4726
		[Flags]
		private enum _AliasTarget
		{
			// Token: 0x0400770A RID: 30474
			None = 0,
			// Token: 0x0400770B RID: 30475
			Register = 1,
			// Token: 0x0400770C RID: 30476
			Device = 2,
			// Token: 0x0400770D RID: 30477
			Network = 4,
			// Token: 0x0400770E RID: 30478
			All = 268435455
		}

		// Token: 0x02001277 RID: 4727
		private struct _AliasValue
		{
			// Token: 0x06008B8F RID: 35727 RVA: 0x002C0D1E File Offset: 0x002BEF1E
			public _AliasValue(ProgrammableChip._AliasTarget target, int index)
			{
				this.Target = target;
				this.Index = index;
			}

			// Token: 0x0400770F RID: 30479
			public readonly ProgrammableChip._AliasTarget Target;

			// Token: 0x04007710 RID: 30480
			public readonly int Index;
		}

		// Token: 0x02001278 RID: 4728
		private struct HelpString
		{
			// Token: 0x06008B90 RID: 35728 RVA: 0x002C0D2E File Offset: 0x002BEF2E
			public HelpString(string str)
			{
				this._string = str;
			}

			// Token: 0x06008B91 RID: 35729 RVA: 0x002C0D37 File Offset: 0x002BEF37
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

			// Token: 0x06008B92 RID: 35730 RVA: 0x002C0D6A File Offset: 0x002BEF6A
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

			// Token: 0x06008B93 RID: 35731 RVA: 0x002C0D9D File Offset: 0x002BEF9D
			public HelpString(ProgrammableChip.HelpString parent, string format)
			{
				this._string = string.Format(format, parent._string);
			}

			// Token: 0x06008B94 RID: 35732 RVA: 0x002C0DB1 File Offset: 0x002BEFB1
			public new readonly string ToString()
			{
				return this._string;
			}

			// Token: 0x06008B95 RID: 35733 RVA: 0x002C0DB9 File Offset: 0x002BEFB9
			public ProgrammableChip.HelpString Var(string variable)
			{
				return new ProgrammableChip.HelpString(variable + "(" + this._string + ")");
			}

			// Token: 0x06008B96 RID: 35734 RVA: 0x002C0DD6 File Offset: 0x002BEFD6
			public static ProgrammableChip.HelpString operator +(ProgrammableChip.HelpString left, ProgrammableChip.HelpString right)
			{
				return new ProgrammableChip.HelpString(left._string + ProgrammableChip.OR.ToString() + right._string);
			}

			// Token: 0x04007711 RID: 30481
			private readonly string _string;
		}

		// Token: 0x02001279 RID: 4729
		public readonly struct Constant : IEquatable<ProgrammableChip.Constant>
		{
			// Token: 0x06008B97 RID: 35735 RVA: 0x002C0DF8 File Offset: 0x002BEFF8
			public bool Equals(ProgrammableChip.Constant other)
			{
				return this.Hash == other.Hash;
			}

			// Token: 0x06008B98 RID: 35736 RVA: 0x002C0E08 File Offset: 0x002BF008
			public override bool Equals(object obj)
			{
				if (obj is ProgrammableChip.Constant)
				{
					ProgrammableChip.Constant other = (ProgrammableChip.Constant)obj;
					return this.Equals(other);
				}
				return false;
			}

			// Token: 0x06008B99 RID: 35737 RVA: 0x002C0E2D File Offset: 0x002BF02D
			public override int GetHashCode()
			{
				return this.Hash;
			}

			// Token: 0x06008B9A RID: 35738 RVA: 0x002C0E35 File Offset: 0x002BF035
			public static bool operator ==(ProgrammableChip.Constant a, string b)
			{
				return !string.IsNullOrEmpty(b) && b.Equals(a.Literal, StringComparison.OrdinalIgnoreCase);
			}

			// Token: 0x06008B9B RID: 35739 RVA: 0x002C0E4E File Offset: 0x002BF04E
			public static bool operator !=(ProgrammableChip.Constant a, string b)
			{
				return string.IsNullOrEmpty(b) || !b.Equals(a.Literal, StringComparison.OrdinalIgnoreCase);
			}

			// Token: 0x06008B9C RID: 35740 RVA: 0x002C0E6C File Offset: 0x002BF06C
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

			// Token: 0x06008B9D RID: 35741 RVA: 0x002C0EDD File Offset: 0x002BF0DD
			public string GetName()
			{
				return "<color=#20B2AA>" + this.Literal + "</color>";
			}

			// Token: 0x06008B9E RID: 35742 RVA: 0x002C0EF4 File Offset: 0x002BF0F4
			public string GetValue()
			{
				if (RocketMath.Approximately(this.Value - Math.Floor(this.Value), 0.0, 0.01))
				{
					return this.Value.ToString("0", CultureInfo.CurrentCulture);
				}
				return this.Value.ToString("0." + new string('#', 8), CultureInfo.CurrentCulture);
			}

			// Token: 0x04007712 RID: 30482
			public readonly string Literal;

			// Token: 0x04007713 RID: 30483
			public readonly string Description;

			// Token: 0x04007714 RID: 30484
			public readonly double Value;

			// Token: 0x04007715 RID: 30485
			public readonly int Hash;
		}

		// Token: 0x0200127A RID: 4730
		private class _LineOfCode
		{
			// Token: 0x06008B9F RID: 35743 RVA: 0x002C0F6C File Offset: 0x002BF16C
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
					case ScriptCommand.sgn:
						if (array.Length != 3)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._SGN_Operation(chip, lineNumber, array[1], array[2]);
						break;
					case ScriptCommand.clamp:
						if (array.Length != 5)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._CLAMP_Operation(chip, lineNumber, array[1], array[2], array[3], array[4]);
						break;
					case ScriptCommand.rol:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ROL_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					case ScriptCommand.ror:
						if (array.Length != 4)
						{
							throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
						}
						this.Operation = new ProgrammableChip._ROR_Operation(chip, lineNumber, array[1], array[2], array[3]);
						break;
					default:
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.UnrecognisedInstruction, lineNumber);
					}
				}
				this.LineOfCode = lineOfCode;
			}

			// Token: 0x06008BA0 RID: 35744 RVA: 0x002C2C6C File Offset: 0x002C0E6C
			public override string ToString()
			{
				return this.LineOfCode;
			}

			// Token: 0x04007716 RID: 30486
			public readonly ProgrammableChip._Operation Operation;

			// Token: 0x04007717 RID: 30487
			public readonly string LineOfCode;
		}

		// Token: 0x0200127B RID: 4731
		private abstract class _Operation
		{
			// Token: 0x06008BA1 RID: 35745 RVA: 0x002C2C74 File Offset: 0x002C0E74
			public _Operation(ProgrammableChip chip, int lineNumber)
			{
				this._Chip = chip;
				this._LineNumber = lineNumber;
			}

			// Token: 0x06008BA2 RID: 35746 RVA: 0x002C2C8A File Offset: 0x002C0E8A
			protected LogicType _GetLogicType(string logicTypeCode)
			{
				if (!Enum.IsDefined(typeof(LogicType), logicTypeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				return (LogicType)Enum.Parse(typeof(LogicType), logicTypeCode);
			}

			// Token: 0x06008BA3 RID: 35747 RVA: 0x002C2CC0 File Offset: 0x002C0EC0
			protected LogicSlotType _GetLogicSlotType(string logicTypeCode)
			{
				if (!Enum.IsDefined(typeof(LogicSlotType), logicTypeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
				}
				return (LogicSlotType)Enum.Parse(typeof(LogicSlotType), logicTypeCode);
			}

			// Token: 0x06008BA4 RID: 35748 RVA: 0x002C2CF6 File Offset: 0x002C0EF6
			protected LogicReagentMode _GetLogicReagentMode(string logicReagentModeCode)
			{
				if (!Enum.IsDefined(typeof(LogicReagentMode), logicReagentModeCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectReagentMode, this._LineNumber);
				}
				return (LogicReagentMode)Enum.Parse(typeof(LogicReagentMode), logicReagentModeCode);
			}

			// Token: 0x06008BA5 RID: 35749 RVA: 0x002C2D2D File Offset: 0x002C0F2D
			protected void _SetDeviceValue(Device device, LogicType logicType, double value)
			{
				if (device == null)
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.DeviceNotSet, this._LineNumber);
				}
				device.SetLogicValue(logicType, value);
			}

			// Token: 0x06008BA6 RID: 35750
			public abstract int Execute(int index);

			// Token: 0x06008BA7 RID: 35751 RVA: 0x002C2D50 File Offset: 0x002C0F50
			protected static ProgrammableChip._Operation.IDeviceVariable _MakeDeviceVariable(ProgrammableChip chip, int lineNumber, string deviceCode)
			{
				if (Regex.IsMatch(deviceCode, "^r+(?:1[0-5]|[0-9])$"))
				{
					return new ProgrammableChip._Operation.DirectDeviceVariable(chip, lineNumber, deviceCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.DeviceIndex | InstructionInclude.Define | InstructionInclude.Enum, false);
				}
				if (chip._Defines.ContainsKey(deviceCode))
				{
					return new ProgrammableChip._Operation.DirectDeviceVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				}
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

			// Token: 0x04007718 RID: 30488
			protected readonly ProgrammableChip _Chip;

			// Token: 0x04007719 RID: 30489
			protected readonly int _LineNumber;

			// Token: 0x0400771A RID: 30490
			protected const string _ZeroString = "0";

			// Token: 0x0200138C RID: 5004
			public class Variable
			{
				// Token: 0x06008D8E RID: 36238 RVA: 0x002CBD80 File Offset: 0x002C9F80
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

				// Token: 0x06008D8F RID: 36239 RVA: 0x002CBDF8 File Offset: 0x002C9FF8
				private int _GetIndex(string rCode, char firstLetter, bool throwException = true)
				{
					int num;
					return this._GetIndex(rCode, firstLetter, out num, throwException);
				}

				// Token: 0x06008D90 RID: 36240 RVA: 0x002CBE10 File Offset: 0x002CA010
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

				// Token: 0x06008D91 RID: 36241 RVA: 0x002CBE8B File Offset: 0x002CA08B
				protected int _GetRegisterIndex(string rCode, out int recurseCount, bool throwException = true)
				{
					return this._GetIndex(rCode, 'r', out recurseCount, throwException);
				}

				// Token: 0x06008D92 RID: 36242 RVA: 0x002CBE98 File Offset: 0x002CA098
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

				// Token: 0x06008D93 RID: 36243 RVA: 0x002CBEF0 File Offset: 0x002CA0F0
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

				// Token: 0x06008D94 RID: 36244 RVA: 0x002CBF3C File Offset: 0x002CA13C
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

				// Token: 0x06008D95 RID: 36245 RVA: 0x002CBF83 File Offset: 0x002CA183
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

				// Token: 0x04007928 RID: 31016
				protected readonly ProgrammableChip _Chip;

				// Token: 0x04007929 RID: 31017
				protected readonly int _LineNumber;

				// Token: 0x0400792A RID: 31018
				protected readonly InstructionInclude _PropertiesToUse;

				// Token: 0x0400792B RID: 31019
				protected readonly int _RegisterIndex = -1;

				// Token: 0x0400792C RID: 31020
				protected readonly int _RegisterRecurse = -1;

				// Token: 0x0400792D RID: 31021
				protected readonly string _Alias;
			}

			// Token: 0x0200138D RID: 5005
			protected class IndexVariable : ProgrammableChip._Operation.IntValuedVariable
			{
				// Token: 0x06008D96 RID: 36246 RVA: 0x002CBFC3 File Offset: 0x002CA1C3
				public IndexVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
				}

				// Token: 0x06008D97 RID: 36247 RVA: 0x002CBFD4 File Offset: 0x002CA1D4
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

				// Token: 0x06008D98 RID: 36248 RVA: 0x002CC034 File Offset: 0x002CA234
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

				// Token: 0x06008D99 RID: 36249 RVA: 0x002CC0B4 File Offset: 0x002CA2B4
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

			// Token: 0x0200138E RID: 5006
			protected class ValueVariable : ProgrammableChip._Operation.Variable
			{
				// Token: 0x06008D9A RID: 36250 RVA: 0x002CC199 File Offset: 0x002CA399
				public ValueVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
				}

				// Token: 0x06008D9B RID: 36251 RVA: 0x002CC1A8 File Offset: 0x002CA3A8
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

				// Token: 0x06008D9C RID: 36252 RVA: 0x002CC264 File Offset: 0x002CA464
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

			// Token: 0x0200138F RID: 5007
			protected class DoubleValueVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x06008D9D RID: 36253 RVA: 0x002CC2FE File Offset: 0x002CA4FE
				public double Get()
				{
					return this._Value;
				}

				// Token: 0x06008D9E RID: 36254 RVA: 0x002CC308 File Offset: 0x002CA508
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

				// Token: 0x06008D9F RID: 36255 RVA: 0x002CC3F4 File Offset: 0x002CA5F4
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

				// Token: 0x06008DA0 RID: 36256 RVA: 0x002CC424 File Offset: 0x002CA624
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

				// Token: 0x06008DA1 RID: 36257 RVA: 0x002CC478 File Offset: 0x002CA678
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

				// Token: 0x06008DA2 RID: 36258 RVA: 0x002CC4C8 File Offset: 0x002CA6C8
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

				// Token: 0x0400792E RID: 31022
				private readonly double _Value = double.NaN;

				// Token: 0x0400792F RID: 31023
				private readonly bool _qNaN;
			}

			// Token: 0x02001390 RID: 5008
			protected class LineNumberVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x06008DA3 RID: 36259 RVA: 0x002CC589 File Offset: 0x002CA789
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

				// Token: 0x06008DA4 RID: 36260 RVA: 0x002CC5C4 File Offset: 0x002CA7C4
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

				// Token: 0x04007930 RID: 31024
				private readonly bool _IsValueSet;

				// Token: 0x04007931 RID: 31025
				private readonly int _Value;
			}

			// Token: 0x02001391 RID: 5009
			protected interface IDeviceVariable
			{
				// Token: 0x06008DA5 RID: 36261
				int GetNetworkIndex();

				// Token: 0x06008DA6 RID: 36262
				ILogicable GetDevice(ICircuitHolder chipCircuitHousing);
			}

			// Token: 0x02001392 RID: 5010
			protected class DirectDeviceVariable : ProgrammableChip._Operation.IntValuedVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x06008DA7 RID: 36263 RVA: 0x002CC68C File Offset: 0x002CA88C
				public DirectDeviceVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						this._DeviceNetwork = base._GetNetworkIndex(code, throwException);
					}
				}

				// Token: 0x06008DA8 RID: 36264 RVA: 0x002CC6BF File Offset: 0x002CA8BF
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x06008DA9 RID: 36265 RVA: 0x002CC6C8 File Offset: 0x002CA8C8
				public ILogicable GetDevice(ICircuitHolder chipCircuitHousing)
				{
					int variableValue = base.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
					return chipCircuitHousing.GetLogicableFromId(variableValue, this._DeviceNetwork);
				}

				// Token: 0x04007932 RID: 31026
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x02001393 RID: 5011
			protected class DeviceAliasVariable : ProgrammableChip._Operation.IndexVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x06008DAA RID: 36266 RVA: 0x002CC6EB File Offset: 0x002CA8EB
				public DeviceAliasVariable(ProgrammableChip chip, int lineNumber, string code, InstructionInclude propertiesToUse, bool throwException = true) : base(chip, lineNumber, code, propertiesToUse, throwException)
				{
					if ((propertiesToUse & InstructionInclude.NetworkIndex) != InstructionInclude.None)
					{
						this._DeviceNetwork = base._GetNetworkIndex(code, throwException);
					}
				}

				// Token: 0x06008DAB RID: 36267 RVA: 0x002CC71E File Offset: 0x002CA91E
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x06008DAC RID: 36268 RVA: 0x002CC728 File Offset: 0x002CA928
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

				// Token: 0x04007933 RID: 31027
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x02001394 RID: 5012
			protected class DeviceIndexVariable : ProgrammableChip._Operation.IndexVariable, ProgrammableChip._Operation.IDeviceVariable
			{
				// Token: 0x06008DAD RID: 36269 RVA: 0x002CC7AC File Offset: 0x002CA9AC
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

				// Token: 0x06008DAE RID: 36270 RVA: 0x002CC814 File Offset: 0x002CAA14
				public int GetNetworkIndex()
				{
					return this._DeviceNetwork;
				}

				// Token: 0x06008DAF RID: 36271 RVA: 0x002CC81C File Offset: 0x002CAA1C
				public ILogicable GetDevice(ICircuitHolder chipCircuitHousing)
				{
					int variableIndex = this.GetVariableIndex(ProgrammableChip._AliasTarget.Device, false);
					return chipCircuitHousing.GetLogicableFromIndex(variableIndex, this._DeviceNetwork);
				}

				// Token: 0x06008DB0 RID: 36272 RVA: 0x002CC840 File Offset: 0x002CAA40
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

				// Token: 0x06008DB1 RID: 36273 RVA: 0x002CC8A4 File Offset: 0x002CAAA4
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

				// Token: 0x04007934 RID: 31028
				protected readonly int _DeviceIndex = -1;

				// Token: 0x04007935 RID: 31029
				protected readonly int _DeviceRecurse = -1;

				// Token: 0x04007936 RID: 31030
				protected readonly int _DeviceNetwork = int.MinValue;
			}

			// Token: 0x02001395 RID: 5013
			protected class IntValuedVariable : ProgrammableChip._Operation.ValueVariable
			{
				// Token: 0x06008DB2 RID: 36274 RVA: 0x002CC924 File Offset: 0x002CAB24
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

				// Token: 0x06008DB3 RID: 36275 RVA: 0x002CCA18 File Offset: 0x002CAC18
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

				// Token: 0x04007937 RID: 31031
				private readonly bool _IsValueSet;

				// Token: 0x04007938 RID: 31032
				private readonly int _Value = -1;
			}

			// Token: 0x02001396 RID: 5014
			protected class EnumValuedVariable<T> : ProgrammableChip._Operation.ValueVariable where T : Enum, IConvertible, new()
			{
				// Token: 0x06008DB4 RID: 36276 RVA: 0x002CCAC4 File Offset: 0x002CACC4
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

				// Token: 0x06008DB5 RID: 36277 RVA: 0x002CCB90 File Offset: 0x002CAD90
				private T GetTypeOf(string logicTypeCode)
				{
					if (!Enum.IsDefined(typeof(T), logicTypeCode))
					{
						throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectLogicType, this._LineNumber);
					}
					return (T)((object)Enum.Parse(typeof(T), logicTypeCode));
				}

				// Token: 0x06008DB6 RID: 36278 RVA: 0x002CCBC8 File Offset: 0x002CADC8
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

				// Token: 0x04007939 RID: 31033
				private readonly bool _IsValueSet;

				// Token: 0x0400793A RID: 31034
				private readonly int _Value = -1;
			}
		}

		// Token: 0x0200127C RID: 4732
		private abstract class _Operation_I : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BA8 RID: 35752 RVA: 0x002C2E61 File Offset: 0x002C1061
			public _Operation_I(ProgrammableChip chip, int lineNumber, string registerStoreCode, string referenceId) : base(chip, lineNumber, registerStoreCode)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400771B RID: 30491
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;
		}

		// Token: 0x0200127D RID: 4733
		private abstract class _Operation_1_0 : ProgrammableChip._Operation
		{
			// Token: 0x06008BA9 RID: 35753 RVA: 0x002C2E7E File Offset: 0x002C107E
			public _Operation_1_0(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber)
			{
				this._Store = new ProgrammableChip._Operation.IndexVariable(chip, lineNumber, registerStoreCode, InstructionInclude.MaskStoreIndex, false);
			}

			// Token: 0x0400771C RID: 30492
			protected ProgrammableChip._Operation.IndexVariable _Store;
		}

		// Token: 0x0200127E RID: 4734
		private abstract class _Operation_1_1 : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BAA RID: 35754 RVA: 0x002C2E98 File Offset: 0x002C1098
			public _Operation_1_1(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code) : base(chip, lineNumber, registerStoreCode)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400771D RID: 30493
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x0200127F RID: 4735
		private abstract class _Operation_1_2 : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008BAB RID: 35755 RVA: 0x002C2EB5 File Offset: 0x002C10B5
			public _Operation_1_2(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code) : base(chip, lineNumber, registerStoreCode, argument1Code)
			{
				this._Argument2 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument2Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400771E RID: 30494
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument2;
		}

		// Token: 0x02001280 RID: 4736
		private abstract class _Operation_1_3 : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BAC RID: 35756 RVA: 0x002C2ED4 File Offset: 0x002C10D4
			public _Operation_1_3(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code, string argument3Code) : base(chip, lineNumber, registerStoreCode, argument1Code, argument2Code)
			{
				this._Argument3 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument3Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x0400771F RID: 30495
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument3;
		}

		// Token: 0x02001281 RID: 4737
		private abstract class _Operation_J_0 : ProgrammableChip._Operation
		{
			// Token: 0x06008BAD RID: 35757 RVA: 0x002C2EF5 File Offset: 0x002C10F5
			public _Operation_J_0(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber)
			{
				this._JumpIndex = new ProgrammableChip._Operation.LineNumberVariable(chip, lineNumber, jumpAddressCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007720 RID: 30496
			protected readonly ProgrammableChip._Operation.LineNumberVariable _JumpIndex;
		}

		// Token: 0x02001282 RID: 4738
		private abstract class _Operation_J_1 : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008BAE RID: 35758 RVA: 0x002C2F10 File Offset: 0x002C1110
			public _Operation_J_1(ProgrammableChip chip, int lineNumber, string argument1Code, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007721 RID: 30497
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x02001283 RID: 4739
		private abstract class _Operation_J_2 : ProgrammableChip._Operation_J_1
		{
			// Token: 0x06008BAF RID: 35759 RVA: 0x002C2F2D File Offset: 0x002C112D
			public _Operation_J_2(ProgrammableChip chip, int lineNumber, string argument1Code, string argument2Code, string jumpAddressCode) : base(chip, lineNumber, argument1Code, jumpAddressCode)
			{
				this._Argument2 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument2Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007722 RID: 30498
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument2;
		}

		// Token: 0x02001284 RID: 4740
		private abstract class _Operation_J_3 : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008BB0 RID: 35760 RVA: 0x002C2F4C File Offset: 0x002C114C
			public _Operation_J_3(ProgrammableChip chip, int lineNumber, string argument1Code, string argument2Code, string argument3Code, string jumpAddressCode) : base(chip, lineNumber, argument1Code, argument2Code, jumpAddressCode)
			{
				this._Argument3 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument3Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x04007723 RID: 30499
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument3;
		}

		// Token: 0x02001285 RID: 4741
		private class _L_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BB1 RID: 35761 RVA: 0x002C2F6D File Offset: 0x002C116D
			public _L_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicTypeCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BB2 RID: 35762 RVA: 0x002C2F9C File Offset: 0x002C119C
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

			// Token: 0x04007724 RID: 30500
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;

			// Token: 0x04007725 RID: 30501
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001286 RID: 4742
		private class _LD_Operation : ProgrammableChip._Operation_I
		{
			// Token: 0x06008BB3 RID: 35763 RVA: 0x002C302C File Offset: 0x002C122C
			public _LD_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string referenceId, string logicTypeCode) : base(chip, lineNumber, registerCode, referenceId)
			{
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BB4 RID: 35764 RVA: 0x002C3050 File Offset: 0x002C1250
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

			// Token: 0x04007726 RID: 30502
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001287 RID: 4743
		private class _S_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BB5 RID: 35765 RVA: 0x002C30DD File Offset: 0x002C12DD
			public _S_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BB6 RID: 35766 RVA: 0x002C311C File Offset: 0x002C131C
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

			// Token: 0x04007727 RID: 30503
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;

			// Token: 0x04007728 RID: 30504
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007729 RID: 30505
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001288 RID: 4744
		private class _SD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BB7 RID: 35767 RVA: 0x002C31AC File Offset: 0x002C13AC
			public _SD_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BB8 RID: 35768 RVA: 0x002C31FC File Offset: 0x002C13FC
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

			// Token: 0x0400772A RID: 30506
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;

			// Token: 0x0400772B RID: 30507
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400772C RID: 30508
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x02001289 RID: 4745
		private class _SS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BB9 RID: 35769 RVA: 0x002C328C File Offset: 0x002C148C
			public _SS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string slotIndex, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotIndex, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BBA RID: 35770 RVA: 0x002C32E8 File Offset: 0x002C14E8
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

			// Token: 0x0400772D RID: 30509
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400772E RID: 30510
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400772F RID: 30511
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007730 RID: 30512
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;
		}

		// Token: 0x0200128A RID: 4746
		private class _SBS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BBB RID: 35771 RVA: 0x002C338C File Offset: 0x002C158C
			public _SBS_Operation(ProgrammableChip chip, int lineNumber, string deviceHash, string slotIndex, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceHash, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotIndex, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BBC RID: 35772 RVA: 0x002C33EC File Offset: 0x002C15EC
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

			// Token: 0x04007731 RID: 30513
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007732 RID: 30514
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007733 RID: 30515
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007734 RID: 30516
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;
		}

		// Token: 0x0200128B RID: 4747
		private class _LB_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BBD RID: 35773 RVA: 0x002C34D0 File Offset: 0x002C16D0
			public _LB_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x06008BBE RID: 35774 RVA: 0x002C3524 File Offset: 0x002C1724
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

			// Token: 0x04007735 RID: 30517
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007736 RID: 30518
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;

			// Token: 0x04007737 RID: 30519
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x0200128C RID: 4748
		private class _LBN_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BBF RID: 35775 RVA: 0x002C3600 File Offset: 0x002C1800
			public _LBN_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string nameCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x06008BC0 RID: 35776 RVA: 0x002C3664 File Offset: 0x002C1864
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

			// Token: 0x04007738 RID: 30520
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007739 RID: 30521
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x0400773A RID: 30522
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;

			// Token: 0x0400773B RID: 30523
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x0200128D RID: 4749
		private class _SB_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BC1 RID: 35777 RVA: 0x002C3748 File Offset: 0x002C1948
			public _SB_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BC2 RID: 35778 RVA: 0x002C3798 File Offset: 0x002C1998
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

			// Token: 0x0400773C RID: 30524
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x0400773D RID: 30525
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x0400773E RID: 30526
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x0200128E RID: 4750
		private class _SBN_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BC3 RID: 35779 RVA: 0x002C3860 File Offset: 0x002C1A60
			public _SBN_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string nameHash, string logicTypeCode, string registerOrValueCode) : base(chip, lineNumber)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameHash, InstructionInclude.MaskDoubleValue, false);
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008BC4 RID: 35780 RVA: 0x002C38C0 File Offset: 0x002C1AC0
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

			// Token: 0x0400773F RID: 30527
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007740 RID: 30528
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x04007741 RID: 30529
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007742 RID: 30530
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x0200128F RID: 4751
		private class _LS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BC5 RID: 35781 RVA: 0x002C39A4 File Offset: 0x002C1BA4
			public _LS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string slotCode, string logicTypeCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
			}

			// Token: 0x06008BC6 RID: 35782 RVA: 0x002C39F0 File Offset: 0x002C1BF0
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

			// Token: 0x04007743 RID: 30531
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x04007744 RID: 30532
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x04007745 RID: 30533
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;
		}

		// Token: 0x02001290 RID: 4752
		private class _LBS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BC7 RID: 35783 RVA: 0x002C3A80 File Offset: 0x002C1C80
			public _LBS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string slotCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x06008BC8 RID: 35784 RVA: 0x002C3AE4 File Offset: 0x002C1CE4
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

			// Token: 0x04007746 RID: 30534
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x04007747 RID: 30535
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x04007748 RID: 30536
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x04007749 RID: 30537
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x02001291 RID: 4753
		private class _LBNS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BC9 RID: 35785 RVA: 0x002C3BC0 File Offset: 0x002C1DC0
			public _LBNS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string nameCode, string slotCode, string logicTypeCode, string logicBatchMode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDoubleValue, false);
				this._NameHash = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, nameCode, InstructionInclude.MaskDoubleValue, false);
				this._SlotIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, slotCode, InstructionInclude.MaskDoubleValue, false);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicSlotType, false);
				this._BatchMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod>(chip, lineNumber, logicBatchMode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicBatchMethod, false);
			}

			// Token: 0x06008BCA RID: 35786 RVA: 0x002C3C38 File Offset: 0x002C1E38
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

			// Token: 0x0400774A RID: 30538
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceHash;

			// Token: 0x0400774B RID: 30539
			protected readonly ProgrammableChip._Operation.IntValuedVariable _NameHash;

			// Token: 0x0400774C RID: 30540
			protected readonly ProgrammableChip._Operation.IntValuedVariable _SlotIndex;

			// Token: 0x0400774D RID: 30541
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicSlotType> _LogicType;

			// Token: 0x0400774E RID: 30542
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicBatchMethod> _BatchMode;
		}

		// Token: 0x02001292 RID: 4754
		private class _LR_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BCB RID: 35787 RVA: 0x002C3D30 File Offset: 0x002C1F30
			public _LR_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode, string logicReagentModeCode, string reagentCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicReagentMode = new ProgrammableChip._Operation.EnumValuedVariable<LogicReagentMode>(chip, lineNumber, logicReagentModeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicReagentMode, false);
				this._ReagentInt = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, reagentCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008BCC RID: 35788 RVA: 0x002C3D7C File Offset: 0x002C1F7C
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

			// Token: 0x0400774F RID: 30543
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x04007750 RID: 30544
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicReagentMode> _LogicReagentMode;

			// Token: 0x04007751 RID: 30545
			protected readonly ProgrammableChip._Operation.IntValuedVariable _ReagentInt;
		}

		// Token: 0x02001293 RID: 4755
		private class _LABEL_Operation : ProgrammableChip._ALIAS_Operation
		{
			// Token: 0x06008BCD RID: 35789 RVA: 0x002C3F0E File Offset: 0x002C210E
			public _LABEL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string labelCode) : base(chip, lineNumber, labelCode, deviceCode)
			{
			}
		}

		// Token: 0x02001294 RID: 4756
		private class _ALIAS_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BCE RID: 35790 RVA: 0x002C3F1C File Offset: 0x002C211C
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

			// Token: 0x06008BCF RID: 35791 RVA: 0x002C3F83 File Offset: 0x002C2183
			public override int Execute(int index)
			{
				return this.Execute(index, true);
			}

			// Token: 0x06008BD0 RID: 35792 RVA: 0x002C3F90 File Offset: 0x002C2190
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

			// Token: 0x04007752 RID: 30546
			private readonly string _AliasCode;

			// Token: 0x04007753 RID: 30547
			private readonly ProgrammableChip._AliasTarget _TargetType;

			// Token: 0x04007754 RID: 30548
			private readonly ProgrammableChip._Operation.IndexVariable _Target;
		}

		// Token: 0x02001295 RID: 4757
		private class _DEFINE_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008BD1 RID: 35793 RVA: 0x002C40D4 File Offset: 0x002C22D4
			public _DEFINE_Operation(ProgrammableChip chip, int lineNumber, string defineCode, string floatCode) : base(chip, lineNumber)
			{
				ProgrammableChip._Operation.DoubleValueVariable doubleValueVariable = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, floatCode, InstructionInclude.MaskDefineValue, true);
				if (chip._Defines.ContainsKey(defineCode))
				{
					throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ExtraDefine, lineNumber);
				}
				chip._Defines.Add(defineCode, doubleValueVariable.Get());
			}

			// Token: 0x06008BD2 RID: 35794 RVA: 0x002C411F File Offset: 0x002C231F
			public override int Execute(int index)
			{
				return index + 1;
			}
		}

		// Token: 0x02001296 RID: 4758
		private class _MOVE_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008BD3 RID: 35795 RVA: 0x002C4124 File Offset: 0x002C2324
			public _MOVE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008BD4 RID: 35796 RVA: 0x002C4134 File Offset: 0x002C2334
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue;
				return index + 1;
			}
		}

		// Token: 0x02001297 RID: 4759
		private class _ADD_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BD5 RID: 35797 RVA: 0x002C416E File Offset: 0x002C236E
			public _ADD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BD6 RID: 35798 RVA: 0x002C4180 File Offset: 0x002C2380
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue + variableValue2;
				return index + 1;
			}
		}

		// Token: 0x02001298 RID: 4760
		private class _SUB_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BD7 RID: 35799 RVA: 0x002C41CA File Offset: 0x002C23CA
			public _SUB_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BD8 RID: 35800 RVA: 0x002C41DC File Offset: 0x002C23DC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue - variableValue2;
				return index + 1;
			}
		}

		// Token: 0x02001299 RID: 4761
		private class _SDSE_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BD9 RID: 35801 RVA: 0x002C4226 File Offset: 0x002C2426
			public _SDSE_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x06008BDA RID: 35802 RVA: 0x002C4240 File Offset: 0x002C2440
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				this._Chip._Registers[variableIndex] = (double)((device == null) ? 0f : 1f);
				return index + 1;
			}

			// Token: 0x04007755 RID: 30549
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;
		}

		// Token: 0x0200129A RID: 4762
		private class _SDNS_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008BDB RID: 35803 RVA: 0x002C4292 File Offset: 0x002C2492
			public _SDNS_Operation(ProgrammableChip chip, int lineNumber, string registerCode, string deviceCode) : base(chip, lineNumber, registerCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x06008BDC RID: 35804 RVA: 0x002C42AC File Offset: 0x002C24AC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				ILogicable device = this._DeviceRef.GetDevice(this._Chip.CircuitHousing);
				this._Chip._Registers[variableIndex] = (double)((device == null) ? 1f : 0f);
				return index + 1;
			}

			// Token: 0x04007756 RID: 30550
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;
		}

		// Token: 0x0200129B RID: 4763
		private class _SLT_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BDD RID: 35805 RVA: 0x002C42FE File Offset: 0x002C24FE
			public _SLT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BDE RID: 35806 RVA: 0x002C4310 File Offset: 0x002C2510
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue < variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x0200129C RID: 4764
		private class _SGT_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BDF RID: 35807 RVA: 0x002C436F File Offset: 0x002C256F
			public _SGT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BE0 RID: 35808 RVA: 0x002C4380 File Offset: 0x002C2580
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue > variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x0200129D RID: 4765
		private class _SLE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BE1 RID: 35809 RVA: 0x002C43DF File Offset: 0x002C25DF
			public _SLE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BE2 RID: 35810 RVA: 0x002C43F0 File Offset: 0x002C25F0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue <= variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x0200129E RID: 4766
		private class _SGE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BE3 RID: 35811 RVA: 0x002C444F File Offset: 0x002C264F
			public _SGE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BE4 RID: 35812 RVA: 0x002C4460 File Offset: 0x002C2660
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue >= variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x0200129F RID: 4767
		private class _SEQ_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BE5 RID: 35813 RVA: 0x002C44BF File Offset: 0x002C26BF
			public _SEQ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BE6 RID: 35814 RVA: 0x002C44D0 File Offset: 0x002C26D0
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue == variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020012A0 RID: 4768
		private class _SNE_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BE7 RID: 35815 RVA: 0x002C452F File Offset: 0x002C272F
			public _SNE_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BE8 RID: 35816 RVA: 0x002C4540 File Offset: 0x002C2740
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue != variableValue2) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020012A1 RID: 4769
		private class _SAP_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008BE9 RID: 35817 RVA: 0x002C459F File Offset: 0x002C279F
			public _SAP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x06008BEA RID: 35818 RVA: 0x002C45B0 File Offset: 0x002C27B0
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

		// Token: 0x020012A2 RID: 4770
		private class _SNA_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008BEB RID: 35819 RVA: 0x002C4644 File Offset: 0x002C2844
			public _SNA_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x06008BEC RID: 35820 RVA: 0x002C4658 File Offset: 0x002C2858
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

		// Token: 0x020012A3 RID: 4771
		private class _SNAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008BED RID: 35821 RVA: 0x002C46EC File Offset: 0x002C28EC
			public _SNAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008BEE RID: 35822 RVA: 0x002C46FC File Offset: 0x002C28FC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = (double.IsNaN(variableValue) ? 1.0 : 0.0);
				return index + 1;
			}
		}

		// Token: 0x020012A4 RID: 4772
		private class _SNANZ_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008BEF RID: 35823 RVA: 0x002C4751 File Offset: 0x002C2951
			public _SNANZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008BF0 RID: 35824 RVA: 0x002C4760 File Offset: 0x002C2960
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = (double.IsNaN(variableValue) ? 0.0 : 1.0);
				return index + 1;
			}
		}

		// Token: 0x020012A5 RID: 4773
		private class _SLTZ_Operation : ProgrammableChip._SLT_Operation
		{
			// Token: 0x06008BF1 RID: 35825 RVA: 0x002C47B5 File Offset: 0x002C29B5
			public _SLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012A6 RID: 4774
		private class _SGTZ_Operation : ProgrammableChip._SGT_Operation
		{
			// Token: 0x06008BF2 RID: 35826 RVA: 0x002C47C7 File Offset: 0x002C29C7
			public _SGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012A7 RID: 4775
		private class _SLEZ_Operation : ProgrammableChip._SLE_Operation
		{
			// Token: 0x06008BF3 RID: 35827 RVA: 0x002C47D9 File Offset: 0x002C29D9
			public _SLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012A8 RID: 4776
		private class _SGEZ_Operation : ProgrammableChip._SGE_Operation
		{
			// Token: 0x06008BF4 RID: 35828 RVA: 0x002C47EB File Offset: 0x002C29EB
			public _SGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012A9 RID: 4777
		private class _SEQZ_Operation : ProgrammableChip._SEQ_Operation
		{
			// Token: 0x06008BF5 RID: 35829 RVA: 0x002C47FD File Offset: 0x002C29FD
			public _SEQZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012AA RID: 4778
		private class _SNEZ_Operation : ProgrammableChip._SNE_Operation
		{
			// Token: 0x06008BF6 RID: 35830 RVA: 0x002C480F File Offset: 0x002C2A0F
			public _SNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0")
			{
			}
		}

		// Token: 0x020012AB RID: 4779
		private class _SAPZ_Operation : ProgrammableChip._SAP_Operation
		{
			// Token: 0x06008BF7 RID: 35831 RVA: 0x002C4821 File Offset: 0x002C2A21
			public _SAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0", registerArgument2Code)
			{
			}
		}

		// Token: 0x020012AC RID: 4780
		private class _SNAZ_Operation : ProgrammableChip._SNA_Operation
		{
			// Token: 0x06008BF8 RID: 35832 RVA: 0x002C4835 File Offset: 0x002C2A35
			public _SNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, "0", registerArgument2Code)
			{
			}
		}

		// Token: 0x020012AD RID: 4781
		private class _EXT_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008BF9 RID: 35833 RVA: 0x002C4849 File Offset: 0x002C2A49
			public _EXT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string arg1Code, string arg2Code, string arg3Code) : base(chip, lineNumber, registerStoreCode, arg1Code, arg2Code, arg3Code)
			{
			}

			// Token: 0x06008BFA RID: 35834 RVA: 0x002C485C File Offset: 0x002C2A5C
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

		// Token: 0x020012AE RID: 4782
		private class _INS_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008BFB RID: 35835 RVA: 0x002C493C File Offset: 0x002C2B3C
			public _INS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string arg1Code, string arg2Code, string arg3Code) : base(chip, lineNumber, registerStoreCode, arg1Code, arg2Code, arg3Code)
			{
			}

			// Token: 0x06008BFC RID: 35836 RVA: 0x002C4950 File Offset: 0x002C2B50
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true);
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

		// Token: 0x020012AF RID: 4783
		private class _AND_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BFD RID: 35837 RVA: 0x002C4A5E File Offset: 0x002C2C5E
			public _AND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008BFE RID: 35838 RVA: 0x002C4A70 File Offset: 0x002C2C70
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				long variableLong2 = this._Argument2.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong & variableLong2);
				return index + 1;
			}
		}

		// Token: 0x020012B0 RID: 4784
		private class _OR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008BFF RID: 35839 RVA: 0x002C4AC1 File Offset: 0x002C2CC1
			public _OR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C00 RID: 35840 RVA: 0x002C4AD0 File Offset: 0x002C2CD0
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

		// Token: 0x020012B1 RID: 4785
		private class _XOR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C01 RID: 35841 RVA: 0x002C4B27 File Offset: 0x002C2D27
			public _XOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C02 RID: 35842 RVA: 0x002C4B38 File Offset: 0x002C2D38
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

		// Token: 0x020012B2 RID: 4786
		private class _NOR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C03 RID: 35843 RVA: 0x002C4B8B File Offset: 0x002C2D8B
			public _NOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C04 RID: 35844 RVA: 0x002C4B9C File Offset: 0x002C2D9C
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

		// Token: 0x020012B3 RID: 4787
		private class _NOT_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C05 RID: 35845 RVA: 0x002C4BF0 File Offset: 0x002C2DF0
			public _NOT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgumentCode) : base(chip, lineNumber, registerStoreCode, registerArgumentCode)
			{
			}

			// Token: 0x06008C06 RID: 35846 RVA: 0x002C4C00 File Offset: 0x002C2E00
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long l = ~this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x020012B4 RID: 4788
		private class _MUL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C07 RID: 35847 RVA: 0x002C4C41 File Offset: 0x002C2E41
			public _MUL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C08 RID: 35848 RVA: 0x002C4C50 File Offset: 0x002C2E50
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue * variableValue2;
				return index + 1;
			}
		}

		// Token: 0x020012B5 RID: 4789
		private class _DIV_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C09 RID: 35849 RVA: 0x002C4C9A File Offset: 0x002C2E9A
			public _DIV_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C0A RID: 35850 RVA: 0x002C4CAC File Offset: 0x002C2EAC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = variableValue / variableValue2;
				return index + 1;
			}
		}

		// Token: 0x020012B6 RID: 4790
		private class _MOD_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C0B RID: 35851 RVA: 0x002C4CF6 File Offset: 0x002C2EF6
			public _MOD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C0C RID: 35852 RVA: 0x002C4D08 File Offset: 0x002C2F08
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

		// Token: 0x020012B7 RID: 4791
		private class _J_Operation : ProgrammableChip._JR_Operation
		{
			// Token: 0x06008C0D RID: 35853 RVA: 0x002C4D6B File Offset: 0x002C2F6B
			public _J_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008C0E RID: 35854 RVA: 0x002C4D76 File Offset: 0x002C2F76
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012B8 RID: 4792
		private class _BLT_Operation : ProgrammableChip._BRLT_Operation
		{
			// Token: 0x06008C0F RID: 35855 RVA: 0x002C4D81 File Offset: 0x002C2F81
			public _BLT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C10 RID: 35856 RVA: 0x002C4D90 File Offset: 0x002C2F90
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012B9 RID: 4793
		private class _BGT_Operation : ProgrammableChip._BRGT_Operation
		{
			// Token: 0x06008C11 RID: 35857 RVA: 0x002C4D9B File Offset: 0x002C2F9B
			public _BGT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C12 RID: 35858 RVA: 0x002C4DAA File Offset: 0x002C2FAA
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012BA RID: 4794
		private class _BLE_Operation : ProgrammableChip._BRLE_Operation
		{
			// Token: 0x06008C13 RID: 35859 RVA: 0x002C4DB5 File Offset: 0x002C2FB5
			public _BLE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C14 RID: 35860 RVA: 0x002C4DC4 File Offset: 0x002C2FC4
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012BB RID: 4795
		private class _BGE_Operation : ProgrammableChip._BRGE_Operation
		{
			// Token: 0x06008C15 RID: 35861 RVA: 0x002C4DCF File Offset: 0x002C2FCF
			public _BGE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C16 RID: 35862 RVA: 0x002C4DDE File Offset: 0x002C2FDE
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012BC RID: 4796
		private class _BLTZ_Operation : ProgrammableChip._BLT_Operation
		{
			// Token: 0x06008C17 RID: 35863 RVA: 0x002C4DE9 File Offset: 0x002C2FE9
			public _BLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012BD RID: 4797
		private class _BGEZ_Operation : ProgrammableChip._BGE_Operation
		{
			// Token: 0x06008C18 RID: 35864 RVA: 0x002C4DFB File Offset: 0x002C2FFB
			public _BGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012BE RID: 4798
		private class _BLEZ_Operation : ProgrammableChip._BLE_Operation
		{
			// Token: 0x06008C19 RID: 35865 RVA: 0x002C4E0D File Offset: 0x002C300D
			public _BLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012BF RID: 4799
		private class _BGTZ_Operation : ProgrammableChip._BGT_Operation
		{
			// Token: 0x06008C1A RID: 35866 RVA: 0x002C4E1F File Offset: 0x002C301F
			public _BGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012C0 RID: 4800
		private class _BDSE_Operation : ProgrammableChip._BRDSE_Operation
		{
			// Token: 0x06008C1B RID: 35867 RVA: 0x002C4E31 File Offset: 0x002C3031
			public _BDSE_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008C1C RID: 35868 RVA: 0x002C4E3E File Offset: 0x002C303E
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C1 RID: 4801
		private class _BDNS_Operation : ProgrammableChip._BRDNS_Operation
		{
			// Token: 0x06008C1D RID: 35869 RVA: 0x002C4E49 File Offset: 0x002C3049
			public _BDNS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008C1E RID: 35870 RVA: 0x002C4E56 File Offset: 0x002C3056
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C2 RID: 4802
		private class _BEQ_Operation : ProgrammableChip._BREQ_Operation
		{
			// Token: 0x06008C1F RID: 35871 RVA: 0x002C4E61 File Offset: 0x002C3061
			public _BEQ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C20 RID: 35872 RVA: 0x002C4E70 File Offset: 0x002C3070
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C3 RID: 4803
		private class _BNE_Operation : ProgrammableChip._BRNE_Operation
		{
			// Token: 0x06008C21 RID: 35873 RVA: 0x002C4E7B File Offset: 0x002C307B
			public _BNE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C22 RID: 35874 RVA: 0x002C4E8A File Offset: 0x002C308A
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C4 RID: 4804
		private class _BAP_Operation : ProgrammableChip._BRAP_Operation
		{
			// Token: 0x06008C23 RID: 35875 RVA: 0x002C4E95 File Offset: 0x002C3095
			public _BAP_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C24 RID: 35876 RVA: 0x002C4EA6 File Offset: 0x002C30A6
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C5 RID: 4805
		private class _BNA_Operation : ProgrammableChip._BRNA_Operation
		{
			// Token: 0x06008C25 RID: 35877 RVA: 0x002C4EB1 File Offset: 0x002C30B1
			public _BNA_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C26 RID: 35878 RVA: 0x002C4EC2 File Offset: 0x002C30C2
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012C6 RID: 4806
		private class _BEQZ_Operation : ProgrammableChip._BEQ_Operation
		{
			// Token: 0x06008C27 RID: 35879 RVA: 0x002C4ECD File Offset: 0x002C30CD
			public _BEQZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012C7 RID: 4807
		private class _BNEZ_Operation : ProgrammableChip._BNE_Operation
		{
			// Token: 0x06008C28 RID: 35880 RVA: 0x002C4EDF File Offset: 0x002C30DF
			public _BNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012C8 RID: 4808
		private class _BAPZ_Operation : ProgrammableChip._BAP_Operation
		{
			// Token: 0x06008C29 RID: 35881 RVA: 0x002C4EF1 File Offset: 0x002C30F1
			public _BAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x020012C9 RID: 4809
		private class _BNAZ_Operation : ProgrammableChip._BNA_Operation
		{
			// Token: 0x06008C2A RID: 35882 RVA: 0x002C4F05 File Offset: 0x002C3105
			public _BNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x020012CA RID: 4810
		private class _JAL_Operation : ProgrammableChip._J_Operation
		{
			// Token: 0x06008C2B RID: 35883 RVA: 0x002C4F19 File Offset: 0x002C3119
			public _JAL_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008C2C RID: 35884 RVA: 0x002C4F24 File Offset: 0x002C3124
			public override int Execute(int index)
			{
				this._Chip._Registers[this._Chip._ReturnAddressIndex] = (double)(index + 1);
				return base.Execute(index);
			}
		}

		// Token: 0x020012CB RID: 4811
		private class _BDSEAL_Operation : ProgrammableChip._BDSE_Operation
		{
			// Token: 0x06008C2D RID: 35885 RVA: 0x002C4F48 File Offset: 0x002C3148
			public _BDSEAL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008C2E RID: 35886 RVA: 0x002C4F58 File Offset: 0x002C3158
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

		// Token: 0x020012CC RID: 4812
		private class _BDNSAL_Operation : ProgrammableChip._BDNS_Operation
		{
			// Token: 0x06008C2F RID: 35887 RVA: 0x002C4F8E File Offset: 0x002C318E
			public _BDNSAL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, deviceCode, jumpAddressCode)
			{
			}

			// Token: 0x06008C30 RID: 35888 RVA: 0x002C4F9C File Offset: 0x002C319C
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

		// Token: 0x020012CD RID: 4813
		private class _BLTAL_Operation : ProgrammableChip._BLT_Operation
		{
			// Token: 0x06008C31 RID: 35889 RVA: 0x002C4FD2 File Offset: 0x002C31D2
			public _BLTAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C32 RID: 35890 RVA: 0x002C4FE4 File Offset: 0x002C31E4
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

		// Token: 0x020012CE RID: 4814
		private class _BGEAL_Operation : ProgrammableChip._BGE_Operation
		{
			// Token: 0x06008C33 RID: 35891 RVA: 0x002C501A File Offset: 0x002C321A
			public _BGEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C34 RID: 35892 RVA: 0x002C502C File Offset: 0x002C322C
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

		// Token: 0x020012CF RID: 4815
		private class _BLEAL_Operation : ProgrammableChip._BLE_Operation
		{
			// Token: 0x06008C35 RID: 35893 RVA: 0x002C5062 File Offset: 0x002C3262
			public _BLEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C36 RID: 35894 RVA: 0x002C5074 File Offset: 0x002C3274
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

		// Token: 0x020012D0 RID: 4816
		private class _BGTAL_Operation : ProgrammableChip._BGT_Operation
		{
			// Token: 0x06008C37 RID: 35895 RVA: 0x002C50AA File Offset: 0x002C32AA
			public _BGTAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C38 RID: 35896 RVA: 0x002C50BC File Offset: 0x002C32BC
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

		// Token: 0x020012D1 RID: 4817
		private class _BLTZAL_Operation : ProgrammableChip._BLTAL_Operation
		{
			// Token: 0x06008C39 RID: 35897 RVA: 0x002C50F2 File Offset: 0x002C32F2
			public _BLTZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012D2 RID: 4818
		private class _BGEZAL_Operation : ProgrammableChip._BGEAL_Operation
		{
			// Token: 0x06008C3A RID: 35898 RVA: 0x002C5104 File Offset: 0x002C3304
			public _BGEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012D3 RID: 4819
		private class _BLEZAL_Operation : ProgrammableChip._BLEAL_Operation
		{
			// Token: 0x06008C3B RID: 35899 RVA: 0x002C5116 File Offset: 0x002C3316
			public _BLEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012D4 RID: 4820
		private class _BGTZAL_Operation : ProgrammableChip._BGTAL_Operation
		{
			// Token: 0x06008C3C RID: 35900 RVA: 0x002C5128 File Offset: 0x002C3328
			public _BGTZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012D5 RID: 4821
		private class _BEQAL_Operation : ProgrammableChip._BEQ_Operation
		{
			// Token: 0x06008C3D RID: 35901 RVA: 0x002C513A File Offset: 0x002C333A
			public _BEQAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C3E RID: 35902 RVA: 0x002C514C File Offset: 0x002C334C
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

		// Token: 0x020012D6 RID: 4822
		private class _BNEAL_Operation : ProgrammableChip._BNE_Operation
		{
			// Token: 0x06008C3F RID: 35903 RVA: 0x002C5182 File Offset: 0x002C3382
			public _BNEAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C40 RID: 35904 RVA: 0x002C5194 File Offset: 0x002C3394
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

		// Token: 0x020012D7 RID: 4823
		private class _BAPAL_Operation : ProgrammableChip._BAP_Operation
		{
			// Token: 0x06008C41 RID: 35905 RVA: 0x002C51CA File Offset: 0x002C33CA
			public _BAPAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C42 RID: 35906 RVA: 0x002C51DC File Offset: 0x002C33DC
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

		// Token: 0x020012D8 RID: 4824
		private class _BNAAL_Operation : ProgrammableChip._BNA_Operation
		{
			// Token: 0x06008C43 RID: 35907 RVA: 0x002C5212 File Offset: 0x002C3412
			public _BNAAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C44 RID: 35908 RVA: 0x002C5224 File Offset: 0x002C3424
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

		// Token: 0x020012D9 RID: 4825
		private class _BEQZAL_Operation : ProgrammableChip._BEQAL_Operation
		{
			// Token: 0x06008C45 RID: 35909 RVA: 0x002C525A File Offset: 0x002C345A
			public _BEQZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012DA RID: 4826
		private class _BNEZAL_Operation : ProgrammableChip._BNEAL_Operation
		{
			// Token: 0x06008C46 RID: 35910 RVA: 0x002C526C File Offset: 0x002C346C
			public _BNEZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012DB RID: 4827
		private class _BAPZAL_Operation : ProgrammableChip._BAPAL_Operation
		{
			// Token: 0x06008C47 RID: 35911 RVA: 0x002C527E File Offset: 0x002C347E
			public _BAPZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x020012DC RID: 4828
		private class _BNAZAL_Operation : ProgrammableChip._BNAAL_Operation
		{
			// Token: 0x06008C48 RID: 35912 RVA: 0x002C5292 File Offset: 0x002C3492
			public _BNAZAL_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C49 RID: 35913 RVA: 0x002C52A8 File Offset: 0x002C34A8
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

		// Token: 0x020012DD RID: 4829
		private class _JR_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008C4A RID: 35914 RVA: 0x002C52DE File Offset: 0x002C34DE
			public _JR_Operation(ProgrammableChip chip, int lineNumber, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
			}

			// Token: 0x06008C4B RID: 35915 RVA: 0x002C52E9 File Offset: 0x002C34E9
			public override int Execute(int index)
			{
				return this.Execute(index, 0);
			}

			// Token: 0x06008C4C RID: 35916 RVA: 0x002C52F3 File Offset: 0x002C34F3
			public int Execute(int index, int offset)
			{
				return index + offset + this._JumpIndex.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
			}
		}

		// Token: 0x020012DE RID: 4830
		private class _BRLT_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C4D RID: 35917 RVA: 0x002C5306 File Offset: 0x002C3506
			public _BRLT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C4E RID: 35918 RVA: 0x002C5318 File Offset: 0x002C3518
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C4F RID: 35919 RVA: 0x002C5330 File Offset: 0x002C3530
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C50 RID: 35920 RVA: 0x002C5348 File Offset: 0x002C3548
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

		// Token: 0x020012DF RID: 4831
		private class _BRGT_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C51 RID: 35921 RVA: 0x002C538E File Offset: 0x002C358E
			public _BRGT_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C52 RID: 35922 RVA: 0x002C53A0 File Offset: 0x002C35A0
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C53 RID: 35923 RVA: 0x002C53B8 File Offset: 0x002C35B8
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C54 RID: 35924 RVA: 0x002C53D0 File Offset: 0x002C35D0
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

		// Token: 0x020012E0 RID: 4832
		private class _BRLE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C55 RID: 35925 RVA: 0x002C5416 File Offset: 0x002C3616
			public _BRLE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C56 RID: 35926 RVA: 0x002C5428 File Offset: 0x002C3628
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C57 RID: 35927 RVA: 0x002C5440 File Offset: 0x002C3640
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C58 RID: 35928 RVA: 0x002C5458 File Offset: 0x002C3658
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

		// Token: 0x020012E1 RID: 4833
		private class _BRGE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C59 RID: 35929 RVA: 0x002C549E File Offset: 0x002C369E
			public _BRGE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C5A RID: 35930 RVA: 0x002C54B0 File Offset: 0x002C36B0
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C5B RID: 35931 RVA: 0x002C54C8 File Offset: 0x002C36C8
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C5C RID: 35932 RVA: 0x002C54E0 File Offset: 0x002C36E0
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

		// Token: 0x020012E2 RID: 4834
		private class _BRLTZ_Operation : ProgrammableChip._BRLT_Operation
		{
			// Token: 0x06008C5D RID: 35933 RVA: 0x002C5526 File Offset: 0x002C3726
			public _BRLTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012E3 RID: 4835
		private class _BRGEZ_Operation : ProgrammableChip._BRGE_Operation
		{
			// Token: 0x06008C5E RID: 35934 RVA: 0x002C5538 File Offset: 0x002C3738
			public _BRGEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012E4 RID: 4836
		private class _BRLEZ_Operation : ProgrammableChip._BRLE_Operation
		{
			// Token: 0x06008C5F RID: 35935 RVA: 0x002C554A File Offset: 0x002C374A
			public _BRLEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012E5 RID: 4837
		private class _BRGTZ_Operation : ProgrammableChip._BRGT_Operation
		{
			// Token: 0x06008C60 RID: 35936 RVA: 0x002C555C File Offset: 0x002C375C
			public _BRGTZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012E6 RID: 4838
		private class _BRDSE_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008C61 RID: 35937 RVA: 0x002C556E File Offset: 0x002C376E
			public _BRDSE_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x06008C62 RID: 35938 RVA: 0x002C5588 File Offset: 0x002C3788
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C63 RID: 35939 RVA: 0x002C55A0 File Offset: 0x002C37A0
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C64 RID: 35940 RVA: 0x002C55B7 File Offset: 0x002C37B7
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

			// Token: 0x04007757 RID: 30551
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;
		}

		// Token: 0x020012E7 RID: 4839
		private class _BRDNS_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008C65 RID: 35941 RVA: 0x002C55EC File Offset: 0x002C37EC
			public _BRDNS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string jumpAddressCode) : base(chip, lineNumber, jumpAddressCode)
			{
				this._DeviceIndex = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
			}

			// Token: 0x06008C66 RID: 35942 RVA: 0x002C5608 File Offset: 0x002C3808
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C67 RID: 35943 RVA: 0x002C5620 File Offset: 0x002C3820
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C68 RID: 35944 RVA: 0x002C5637 File Offset: 0x002C3837
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

			// Token: 0x04007758 RID: 30552
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceIndex;
		}

		// Token: 0x020012E8 RID: 4840
		private class _BREQ_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C69 RID: 35945 RVA: 0x002C566C File Offset: 0x002C386C
			public _BREQ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C6A RID: 35946 RVA: 0x002C567C File Offset: 0x002C387C
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C6B RID: 35947 RVA: 0x002C5694 File Offset: 0x002C3894
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C6C RID: 35948 RVA: 0x002C56AC File Offset: 0x002C38AC
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

		// Token: 0x020012E9 RID: 4841
		private class _BRNE_Operation : ProgrammableChip._Operation_J_2
		{
			// Token: 0x06008C6D RID: 35949 RVA: 0x002C56F2 File Offset: 0x002C38F2
			public _BRNE_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C6E RID: 35950 RVA: 0x002C5704 File Offset: 0x002C3904
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C6F RID: 35951 RVA: 0x002C571C File Offset: 0x002C391C
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C70 RID: 35952 RVA: 0x002C5734 File Offset: 0x002C3934
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

		// Token: 0x020012EA RID: 4842
		private class _BRAP_Operation : ProgrammableChip._Operation_J_3
		{
			// Token: 0x06008C71 RID: 35953 RVA: 0x002C577A File Offset: 0x002C397A
			public _BRAP_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C72 RID: 35954 RVA: 0x002C578C File Offset: 0x002C398C
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C73 RID: 35955 RVA: 0x002C57A4 File Offset: 0x002C39A4
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C74 RID: 35956 RVA: 0x002C57BC File Offset: 0x002C39BC
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

		// Token: 0x020012EB RID: 4843
		private class _BRNA_Operation : ProgrammableChip._Operation_J_3
		{
			// Token: 0x06008C75 RID: 35957 RVA: 0x002C5839 File Offset: 0x002C3A39
			public _BRNA_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, registerArgument2Code, registerArgument3Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C76 RID: 35958 RVA: 0x002C584C File Offset: 0x002C3A4C
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C77 RID: 35959 RVA: 0x002C5864 File Offset: 0x002C3A64
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C78 RID: 35960 RVA: 0x002C587C File Offset: 0x002C3A7C
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

		// Token: 0x020012EC RID: 4844
		private class _BREQZ_Operation : ProgrammableChip._BREQ_Operation
		{
			// Token: 0x06008C79 RID: 35961 RVA: 0x002C58F9 File Offset: 0x002C3AF9
			public _BREQZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012ED RID: 4845
		private class _BNAN_Operation : ProgrammableChip._BRNAN_Operation
		{
			// Token: 0x06008C7A RID: 35962 RVA: 0x002C590B File Offset: 0x002C3B0B
			public _BNAN_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C7B RID: 35963 RVA: 0x002C5918 File Offset: 0x002C3B18
			public override int Execute(int index)
			{
				return base.Execute(index, -index);
			}
		}

		// Token: 0x020012EE RID: 4846
		private class _BRNAN_Operation : ProgrammableChip._Operation_J_1
		{
			// Token: 0x06008C7C RID: 35964 RVA: 0x002C5923 File Offset: 0x002C3B23
			public _BRNAN_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, jumpAddressCode)
			{
			}

			// Token: 0x06008C7D RID: 35965 RVA: 0x002C5930 File Offset: 0x002C3B30
			public override int Execute(int index)
			{
				bool flag;
				return this.Execute(index, out flag, 0);
			}

			// Token: 0x06008C7E RID: 35966 RVA: 0x002C5948 File Offset: 0x002C3B48
			public int Execute(int index, int offset)
			{
				bool flag;
				return this.Execute(index, out flag, offset);
			}

			// Token: 0x06008C7F RID: 35967 RVA: 0x002C595F File Offset: 0x002C3B5F
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

		// Token: 0x020012EF RID: 4847
		private class _BRNEZ_Operation : ProgrammableChip._BRNE_Operation
		{
			// Token: 0x06008C80 RID: 35968 RVA: 0x002C5990 File Offset: 0x002C3B90
			public _BRNEZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", jumpAddressCode)
			{
			}
		}

		// Token: 0x020012F0 RID: 4848
		private class _BRAPZ_Operation : ProgrammableChip._BRAP_Operation
		{
			// Token: 0x06008C81 RID: 35969 RVA: 0x002C59A2 File Offset: 0x002C3BA2
			public _BRAPZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x020012F1 RID: 4849
		private class _BRNAZ_Operation : ProgrammableChip._BRNA_Operation
		{
			// Token: 0x06008C82 RID: 35970 RVA: 0x002C59B6 File Offset: 0x002C3BB6
			public _BRNAZ_Operation(ProgrammableChip chip, int lineNumber, string registerArgument1Code, string registerArgument2Code, string jumpAddressCode) : base(chip, lineNumber, registerArgument1Code, "0", registerArgument2Code, jumpAddressCode)
			{
			}
		}

		// Token: 0x020012F2 RID: 4850
		private class _SQRT_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C83 RID: 35971 RVA: 0x002C59CA File Offset: 0x002C3BCA
			public _SQRT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C84 RID: 35972 RVA: 0x002C59D8 File Offset: 0x002C3BD8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Sqrt(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012F3 RID: 4851
		private class _ROUND_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C85 RID: 35973 RVA: 0x002C5A17 File Offset: 0x002C3C17
			public _ROUND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C86 RID: 35974 RVA: 0x002C5A24 File Offset: 0x002C3C24
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Round(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012F4 RID: 4852
		private class _TRUNC_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C87 RID: 35975 RVA: 0x002C5A63 File Offset: 0x002C3C63
			public _TRUNC_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C88 RID: 35976 RVA: 0x002C5A70 File Offset: 0x002C3C70
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Truncate(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012F5 RID: 4853
		private class _CEIL_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C89 RID: 35977 RVA: 0x002C5AAF File Offset: 0x002C3CAF
			public _CEIL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C8A RID: 35978 RVA: 0x002C5ABC File Offset: 0x002C3CBC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Ceiling(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012F6 RID: 4854
		private class _FLOOR_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C8B RID: 35979 RVA: 0x002C5AFB File Offset: 0x002C3CFB
			public _FLOOR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C8C RID: 35980 RVA: 0x002C5B08 File Offset: 0x002C3D08
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Floor(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012F7 RID: 4855
		private class _MAX_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C8D RID: 35981 RVA: 0x002C5B47 File Offset: 0x002C3D47
			public _MAX_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C8E RID: 35982 RVA: 0x002C5B58 File Offset: 0x002C3D58
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Max(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x020012F8 RID: 4856
		private class _MIN_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C8F RID: 35983 RVA: 0x002C5BA6 File Offset: 0x002C3DA6
			public _MIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C90 RID: 35984 RVA: 0x002C5BB8 File Offset: 0x002C3DB8
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Min(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x020012F9 RID: 4857
		private class _POW_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008C91 RID: 35985 RVA: 0x002C5C06 File Offset: 0x002C3E06
			public _POW_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008C92 RID: 35986 RVA: 0x002C5C18 File Offset: 0x002C3E18
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Pow(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x020012FA RID: 4858
		private class _LERP_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008C93 RID: 35987 RVA: 0x002C5C66 File Offset: 0x002C3E66
			public _LERP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x06008C94 RID: 35988 RVA: 0x002C5C78 File Offset: 0x002C3E78
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

		// Token: 0x020012FB RID: 4859
		private class _ABS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C95 RID: 35989 RVA: 0x002C5CD5 File Offset: 0x002C3ED5
			public _ABS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C96 RID: 35990 RVA: 0x002C5CE4 File Offset: 0x002C3EE4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Abs(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012FC RID: 4860
		private class _LOG_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C97 RID: 35991 RVA: 0x002C5D23 File Offset: 0x002C3F23
			public _LOG_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C98 RID: 35992 RVA: 0x002C5D30 File Offset: 0x002C3F30
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Log(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012FD RID: 4861
		private class _EXP_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008C99 RID: 35993 RVA: 0x002C5D6F File Offset: 0x002C3F6F
			public _EXP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008C9A RID: 35994 RVA: 0x002C5D7C File Offset: 0x002C3F7C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Exp(variableValue);
				return index + 1;
			}
		}

		// Token: 0x020012FE RID: 4862
		private class _RAND_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008C9C RID: 35996 RVA: 0x002C5DC7 File Offset: 0x002C3FC7
			public _RAND_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x06008C9D RID: 35997 RVA: 0x002C5DD4 File Offset: 0x002C3FD4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip._RAND_Operation._RandomNumberGenerator.NextDouble();
				return index + 1;
			}

			// Token: 0x04007759 RID: 30553
			private static readonly System.Random _RandomNumberGenerator = new System.Random();
		}

		// Token: 0x020012FF RID: 4863
		private class _HCF_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008C9E RID: 35998 RVA: 0x002C5E09 File Offset: 0x002C4009
			public _HCF_Operation(ProgrammableChip chip, int lineNumber) : base(chip, lineNumber)
			{
			}

			// Token: 0x06008C9F RID: 35999 RVA: 0x002C5E13 File Offset: 0x002C4013
			public override int Execute(int index)
			{
				this._Chip.HaltAndCatchFire();
				throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.ChipCatchingFire, this._LineNumber);
			}
		}

		// Token: 0x02001300 RID: 4864
		private class _YIELD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CA0 RID: 36000 RVA: 0x002C5E2D File Offset: 0x002C402D
			public _YIELD_Operation(ProgrammableChip chip, int lineNumber) : base(chip, lineNumber)
			{
			}

			// Token: 0x06008CA1 RID: 36001 RVA: 0x002C5E37 File Offset: 0x002C4037
			public override int Execute(int index)
			{
				return -index - 1;
			}
		}

		// Token: 0x02001301 RID: 4865
		private class _NOOP_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CA2 RID: 36002 RVA: 0x002C5E3D File Offset: 0x002C403D
			public _NOOP_Operation(ProgrammableChip chip, int lineNUmber) : base(chip, lineNUmber)
			{
			}

			// Token: 0x06008CA3 RID: 36003 RVA: 0x002C5E47 File Offset: 0x002C4047
			public override int Execute(int index)
			{
				return index + 1;
			}
		}

		// Token: 0x02001302 RID: 4866
		private class _POP_Operation : ProgrammableChip._PEEK_Operation
		{
			// Token: 0x06008CA4 RID: 36004 RVA: 0x002C5E4C File Offset: 0x002C404C
			public _POP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x06008CA5 RID: 36005 RVA: 0x002C5E58 File Offset: 0x002C4058
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

		// Token: 0x02001303 RID: 4867
		private class _PEEK_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008CA6 RID: 36006 RVA: 0x002C5F2C File Offset: 0x002C412C
			public _PEEK_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode) : base(chip, lineNumber, registerStoreCode)
			{
			}

			// Token: 0x06008CA7 RID: 36007 RVA: 0x002C5F38 File Offset: 0x002C4138
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

		// Token: 0x02001304 RID: 4868
		private class _PUSH_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CA8 RID: 36008 RVA: 0x002C5FE6 File Offset: 0x002C41E6
			public _PUSH_Operation(ProgrammableChip chip, int lineNumber, string argument1Code) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CA9 RID: 36009 RVA: 0x002C6004 File Offset: 0x002C4204
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

			// Token: 0x0400775A RID: 30554
			protected ProgrammableChip._Operation.DoubleValueVariable _Argument1;
		}

		// Token: 0x02001305 RID: 4869
		private class _CLR_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CAA RID: 36010 RVA: 0x002C60CC File Offset: 0x002C42CC
			public _CLR_Operation(ProgrammableChip chip, int lineNumber, string deviceCode) : base(chip, lineNumber)
			{
				this._DeviceIndex = new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
			}

			// Token: 0x06008CAB RID: 36011 RVA: 0x002C60EC File Offset: 0x002C42EC
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

			// Token: 0x0400775B RID: 30555
			protected readonly ProgrammableChip._Operation.DeviceIndexVariable _DeviceIndex;
		}

		// Token: 0x02001306 RID: 4870
		private class _CLRD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CAC RID: 36012 RVA: 0x002C614C File Offset: 0x002C434C
			public _CLRD_Operation(ProgrammableChip chip, int lineNumber, string referenceId) : base(chip, lineNumber)
			{
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CAD RID: 36013 RVA: 0x002C6168 File Offset: 0x002C4368
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

			// Token: 0x0400775C RID: 30556
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;
		}

		// Token: 0x02001307 RID: 4871
		private class _GET_Operation : ProgrammableChip._Operation_1_0
		{
			// Token: 0x06008CAE RID: 36014 RVA: 0x002C61C8 File Offset: 0x002C43C8
			public _GET_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string deviceCode, string stackIndexCode) : base(chip, lineNumber, registerStoreCode)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CAF RID: 36015 RVA: 0x002C61F4 File Offset: 0x002C43F4
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

			// Token: 0x0400775D RID: 30557
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400775E RID: 30558
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001308 RID: 4872
		private class _PUT_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CB0 RID: 36016 RVA: 0x002C6272 File Offset: 0x002C4472
			public _PUT_Operation(ProgrammableChip chip, int lineNumber, string argument1Code, string deviceCode, string stackIndexCode) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CB1 RID: 36017 RVA: 0x002C62B0 File Offset: 0x002C44B0
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

			// Token: 0x0400775F RID: 30559
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x04007760 RID: 30560
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007761 RID: 30561
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x02001309 RID: 4873
		private class _GETD_Operation : ProgrammableChip._Operation_I
		{
			// Token: 0x06008CB2 RID: 36018 RVA: 0x002C6398 File Offset: 0x002C4598
			public _GETD_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string idCode, string stackIndexCode) : base(chip, lineNumber, registerStoreCode, idCode)
			{
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CB3 RID: 36019 RVA: 0x002C63B8 File Offset: 0x002C45B8
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

			// Token: 0x04007762 RID: 30562
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x0200130A RID: 4874
		private class _PUTD_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CB4 RID: 36020 RVA: 0x002C6444 File Offset: 0x002C4644
			public _PUTD_Operation(ProgrammableChip chip, int lineNumber, string argument1Code, string referenceId, string stackIndexCode) : base(chip, lineNumber)
			{
				this._Argument1 = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, argument1Code, InstructionInclude.MaskDoubleValue, false);
				this._DeviceId = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, referenceId, InstructionInclude.MaskDoubleValue, false);
				this._StackIndex = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CB5 RID: 36021 RVA: 0x002C6484 File Offset: 0x002C4684
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

			// Token: 0x04007763 RID: 30563
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _Argument1;

			// Token: 0x04007764 RID: 30564
			protected readonly ProgrammableChip._Operation.IntValuedVariable _DeviceId;

			// Token: 0x04007765 RID: 30565
			protected readonly ProgrammableChip._Operation.IntValuedVariable _StackIndex;
		}

		// Token: 0x0200130B RID: 4875
		private class _POKE_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CB6 RID: 36022 RVA: 0x002C6580 File Offset: 0x002C4780
			public _POKE_Operation(ProgrammableChip chip, int lineNumber, string stackIndexCode, string valueCode) : base(chip, lineNumber)
			{
				this._Index = new ProgrammableChip._Operation.IntValuedVariable(chip, lineNumber, stackIndexCode, InstructionInclude.MaskDoubleValue, false);
				this._Value = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, valueCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CB7 RID: 36023 RVA: 0x002C65B0 File Offset: 0x002C47B0
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

			// Token: 0x04007766 RID: 30566
			protected ProgrammableChip._Operation.IntValuedVariable _Index;

			// Token: 0x04007767 RID: 30567
			protected ProgrammableChip._Operation.DoubleValueVariable _Value;
		}

		// Token: 0x0200130C RID: 4876
		private class _SELECT_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008CB8 RID: 36024 RVA: 0x002C661A File Offset: 0x002C481A
			public _SELECT_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string argument1Code, string argument2Code, string argument3Code) : base(chip, lineNumber, registerStoreCode, argument1Code, argument2Code, argument3Code)
			{
			}

			// Token: 0x06008CB9 RID: 36025 RVA: 0x002C662C File Offset: 0x002C482C
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

		// Token: 0x0200130D RID: 4877
		private class _SLEEP_Operation : ProgrammableChip._Operation
		{
			// Token: 0x06008CBA RID: 36026 RVA: 0x002C6691 File Offset: 0x002C4891
			public _SLEEP_Operation(ProgrammableChip chip, int lineNumber, string sleepDurationCode) : base(chip, lineNumber)
			{
				this._SleepDuration = new ProgrammableChip._Operation.DoubleValueVariable(chip, lineNumber, sleepDurationCode, InstructionInclude.MaskDoubleValue, false);
			}

			// Token: 0x06008CBB RID: 36027 RVA: 0x002C66BC File Offset: 0x002C48BC
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

			// Token: 0x04007768 RID: 30568
			protected readonly ProgrammableChip._Operation.DoubleValueVariable _SleepDuration;

			// Token: 0x04007769 RID: 30569
			public float LastTimeSet;

			// Token: 0x0400776A RID: 30570
			public double SleepDurationRemaining = double.NaN;
		}

		// Token: 0x0200130E RID: 4878
		private class _SRL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CBC RID: 36028 RVA: 0x002C674F File Offset: 0x002C494F
			public _SRL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CBD RID: 36029 RVA: 0x002C6760 File Offset: 0x002C4960
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong >> variableInt);
				return index + 1;
			}
		}

		// Token: 0x0200130F RID: 4879
		private class _SRA_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CBE RID: 36030 RVA: 0x002C67B3 File Offset: 0x002C49B3
			public _SRA_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CBF RID: 36031 RVA: 0x002C67C4 File Offset: 0x002C49C4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long variableLong = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, true, true);
				int variableInt = this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(variableLong >> variableInt);
				return index + 1;
			}
		}

		// Token: 0x02001310 RID: 4880
		private class _SLA_SLL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CC0 RID: 36032 RVA: 0x002C6817 File Offset: 0x002C4A17
			public _SLA_SLL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CC1 RID: 36033 RVA: 0x002C6828 File Offset: 0x002C4A28
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

		// Token: 0x02001311 RID: 4881
		private class _ROL_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CC2 RID: 36034 RVA: 0x002C687B File Offset: 0x002C4A7B
			public _ROL_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CC3 RID: 36035 RVA: 0x002C688C File Offset: 0x002C4A8C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long num = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true) & 18014398509481983L;
				int num2 = (this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true) % 54 + 54) % 54;
				long l = (num << num2 | num >> 54 - num2) & 18014398509481983L;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x02001312 RID: 4882
		private class _ROR_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CC4 RID: 36036 RVA: 0x002C6908 File Offset: 0x002C4B08
			public _ROR_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CC5 RID: 36037 RVA: 0x002C6918 File Offset: 0x002C4B18
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				long num = this._Argument1.GetVariableLong(ProgrammableChip._AliasTarget.Register, false, true) & 18014398509481983L;
				int num2 = (this._Argument2.GetVariableInt(ProgrammableChip._AliasTarget.Register, true) % 54 + 54) % 54;
				long l = (num >> num2 | num << 54 - num2) & 18014398509481983L;
				this._Chip._Registers[variableIndex] = ProgrammableChip.LongToDouble(l);
				return index + 1;
			}
		}

		// Token: 0x02001313 RID: 4883
		private class _SGN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CC6 RID: 36038 RVA: 0x002C6994 File Offset: 0x002C4B94
			public _SGN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CC7 RID: 36039 RVA: 0x002C69A4 File Offset: 0x002C4BA4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = ((variableValue > 0.0) ? 1.0 : ((variableValue < 0.0) ? -1.0 : 0.0));
				return index + 1;
			}
		}

		// Token: 0x02001314 RID: 4884
		private class _CLAMP_Operation : ProgrammableChip._Operation_1_3
		{
			// Token: 0x06008CC8 RID: 36040 RVA: 0x002C6A14 File Offset: 0x002C4C14
			public _CLAMP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code, string registerArgument3Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code, registerArgument3Code)
			{
			}

			// Token: 0x06008CC9 RID: 36041 RVA: 0x002C6A28 File Offset: 0x002C4C28
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue3 = this._Argument3.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Min(Math.Max(variableValue, variableValue2), variableValue3);
				return index + 1;
			}
		}

		// Token: 0x02001315 RID: 4885
		private class _SIN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CCA RID: 36042 RVA: 0x002C6A8A File Offset: 0x002C4C8A
			public _SIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CCB RID: 36043 RVA: 0x002C6A98 File Offset: 0x002C4C98
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Sin(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001316 RID: 4886
		private class _ASIN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CCC RID: 36044 RVA: 0x002C6AD7 File Offset: 0x002C4CD7
			public _ASIN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CCD RID: 36045 RVA: 0x002C6AE4 File Offset: 0x002C4CE4
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Asin(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001317 RID: 4887
		private class _TAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CCE RID: 36046 RVA: 0x002C6B23 File Offset: 0x002C4D23
			public _TAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CCF RID: 36047 RVA: 0x002C6B30 File Offset: 0x002C4D30
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Tan(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001318 RID: 4888
		private class _ATAN_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CD0 RID: 36048 RVA: 0x002C6B6F File Offset: 0x002C4D6F
			public _ATAN_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CD1 RID: 36049 RVA: 0x002C6B7C File Offset: 0x002C4D7C
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Atan(variableValue);
				return index + 1;
			}
		}

		// Token: 0x02001319 RID: 4889
		private class _ATAN2_Operation : ProgrammableChip._Operation_1_2
		{
			// Token: 0x06008CD2 RID: 36050 RVA: 0x002C6BBB File Offset: 0x002C4DBB
			public _ATAN2_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code, string registerArgument2Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code, registerArgument2Code)
			{
			}

			// Token: 0x06008CD3 RID: 36051 RVA: 0x002C6BCC File Offset: 0x002C4DCC
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				double variableValue2 = this._Argument2.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Atan2(variableValue, variableValue2);
				return index + 1;
			}
		}

		// Token: 0x0200131A RID: 4890
		private class _COS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CD4 RID: 36052 RVA: 0x002C6C1A File Offset: 0x002C4E1A
			public _COS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CD5 RID: 36053 RVA: 0x002C6C28 File Offset: 0x002C4E28
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Cos(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200131B RID: 4891
		private class _ACOS_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CD6 RID: 36054 RVA: 0x002C6C67 File Offset: 0x002C4E67
			public _ACOS_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string registerArgument1Code) : base(chip, lineNumber, registerStoreCode, registerArgument1Code)
			{
			}

			// Token: 0x06008CD7 RID: 36055 RVA: 0x002C6C74 File Offset: 0x002C4E74
			public override int Execute(int index)
			{
				int variableIndex = this._Store.GetVariableIndex(ProgrammableChip._AliasTarget.Register, true);
				double variableValue = this._Argument1.GetVariableValue(ProgrammableChip._AliasTarget.Register, true);
				this._Chip._Registers[variableIndex] = Math.Acos(variableValue);
				return index + 1;
			}
		}

		// Token: 0x0200131C RID: 4892
		private class _BDNVS_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008CD8 RID: 36056 RVA: 0x002C6CB3 File Offset: 0x002C4EB3
			public _BDNVS_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string argument1Code) : base(chip, lineNumber, argument1Code)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008CD9 RID: 36057 RVA: 0x002C6CE4 File Offset: 0x002C4EE4
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

			// Token: 0x0400776B RID: 30571
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400776C RID: 30572
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x0200131D RID: 4893
		private class _BDNVL_Operation : ProgrammableChip._Operation_J_0
		{
			// Token: 0x06008CDA RID: 36058 RVA: 0x002C6D51 File Offset: 0x002C4F51
			public _BDNVL_Operation(ProgrammableChip chip, int lineNumber, string deviceCode, string logicTypeCode, string argument1Code) : base(chip, lineNumber, argument1Code)
			{
				this._DeviceRef = ProgrammableChip._Operation._MakeDeviceVariable(chip, lineNumber, deviceCode);
				this._LogicType = new ProgrammableChip._Operation.EnumValuedVariable<LogicType>(chip, lineNumber, logicTypeCode, InstructionInclude.RegisterIndex | InstructionInclude.Alias | InstructionInclude.Value | InstructionInclude.JumpTag | InstructionInclude.Define | InstructionInclude.Enum | InstructionInclude.LogicType, false);
			}

			// Token: 0x06008CDB RID: 36059 RVA: 0x002C6D80 File Offset: 0x002C4F80
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

			// Token: 0x0400776D RID: 30573
			protected readonly ProgrammableChip._Operation.IDeviceVariable _DeviceRef;

			// Token: 0x0400776E RID: 30574
			protected readonly ProgrammableChip._Operation.EnumValuedVariable<LogicType> _LogicType;
		}

		// Token: 0x0200131E RID: 4894
		private class _RMAP_Operation : ProgrammableChip._Operation_1_1
		{
			// Token: 0x06008CDC RID: 36060 RVA: 0x002C6DED File Offset: 0x002C4FED
			public _RMAP_Operation(ProgrammableChip chip, int lineNumber, string registerStoreCode, string deviceCode, string argument1Code) : base(chip, lineNumber, registerStoreCode, argument1Code)
			{
				this._DeviceIndex = new ProgrammableChip._Operation.DeviceIndexVariable(chip, lineNumber, deviceCode, InstructionInclude.MaskDeviceIndex, false);
			}

			// Token: 0x06008CDD RID: 36061 RVA: 0x002C6E10 File Offset: 0x002C5010
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

			// Token: 0x0400776F RID: 30575
			protected readonly ProgrammableChip._Operation.DeviceIndexVariable _DeviceIndex;
		}

		// Token: 0x0200131F RID: 4895
		public class ScriptEnum<T> : IScriptEnum where T : struct, Enum, IConvertible
		{
			// Token: 0x06008CDE RID: 36062 RVA: 0x002C6EA4 File Offset: 0x002C50A4
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

			// Token: 0x06008CDF RID: 36063 RVA: 0x002C6F20 File Offset: 0x002C5120
			public int Count()
			{
				return this._types.Length;
			}

			// Token: 0x06008CE0 RID: 36064 RVA: 0x002C6F2A File Offset: 0x002C512A
			public bool IsDeprecated(int i)
			{
				Func<T, bool> isDeprecated = this._isDeprecated;
				return isDeprecated != null && isDeprecated(this._types[i]);
			}

			// Token: 0x06008CE1 RID: 36065 RVA: 0x002C6F4C File Offset: 0x002C514C
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

			// Token: 0x06008CE2 RID: 36066 RVA: 0x002C70CC File Offset: 0x002C52CC
			public bool TryParse(string searchText)
			{
				T t;
				return Enum.TryParse<T>(searchText, out t);
			}

			// Token: 0x06008CE3 RID: 36067 RVA: 0x002C70E1 File Offset: 0x002C52E1
			public bool IsHashType(int hash)
			{
				return this._typeHash == hash;
			}

			// Token: 0x06008CE4 RID: 36068 RVA: 0x002C70EC File Offset: 0x002C52EC
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

			// Token: 0x06008CE5 RID: 36069 RVA: 0x002C7169 File Offset: 0x002C5369
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

			// Token: 0x06008CE6 RID: 36070 RVA: 0x002C71A9 File Offset: 0x002C53A9
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

			// Token: 0x04007770 RID: 30576
			private readonly InstructionInclude _includeType;

			// Token: 0x04007771 RID: 30577
			private readonly T[] _types;

			// Token: 0x04007772 RID: 30578
			private readonly int _typeHash;

			// Token: 0x04007773 RID: 30579
			private readonly string[] _names;

			// Token: 0x04007774 RID: 30580
			private readonly Func<T, bool> _isDeprecated;

			// Token: 0x04007775 RID: 30581
			private readonly Func<T, string> _getDescription;

			// Token: 0x04007776 RID: 30582
			private readonly string _color;
		}

		// Token: 0x02001320 RID: 4896
		public class BasicEnum<T> : IScriptEnum where T : struct, Enum, IConvertible
		{
			// Token: 0x06008CE7 RID: 36071 RVA: 0x002C71E8 File Offset: 0x002C53E8
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

			// Token: 0x06008CE8 RID: 36072 RVA: 0x002C728C File Offset: 0x002C548C
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

			// Token: 0x06008CE9 RID: 36073 RVA: 0x002C73A9 File Offset: 0x002C55A9
			public int Count()
			{
				return this._types.Length;
			}

			// Token: 0x06008CEA RID: 36074 RVA: 0x002C73B3 File Offset: 0x002C55B3
			public bool IsDeprecated(int i)
			{
				Func<T, bool> isDeprecated = this._isDeprecated;
				return isDeprecated != null && isDeprecated(this._types[i]);
			}

			// Token: 0x06008CEB RID: 36075 RVA: 0x002C73D4 File Offset: 0x002C55D4
			public bool TryParse(string searchText)
			{
				T t;
				return Enum.TryParse<T>(searchText, out t);
			}

			// Token: 0x06008CEC RID: 36076 RVA: 0x002C73E9 File Offset: 0x002C55E9
			public bool IsHashType(int hash)
			{
				return this._typeHash == hash;
			}

			// Token: 0x06008CED RID: 36077 RVA: 0x002C73F4 File Offset: 0x002C55F4
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

			// Token: 0x06008CEE RID: 36078 RVA: 0x002C746C File Offset: 0x002C566C
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

			// Token: 0x06008CEF RID: 36079 RVA: 0x002C74E8 File Offset: 0x002C56E8
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

			// Token: 0x04007777 RID: 30583
			private readonly T[] _types;

			// Token: 0x04007778 RID: 30584
			private readonly string[] _names;

			// Token: 0x04007779 RID: 30585
			private readonly string _color;

			// Token: 0x0400777A RID: 30586
			private readonly string _typeString;

			// Token: 0x0400777B RID: 30587
			private readonly int _typeHash;

			// Token: 0x0400777C RID: 30588
			private readonly Func<T, bool> _isDeprecated;
		}
	}
}
