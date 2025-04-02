#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;

#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	public partial class MainStrategy : Strategy
	{
		public double UpperWick(int offset)
		{
			if(Opens[0][offset] < Closes[0][offset]) ///long
			{
				return Highs[0][offset]-Closes[0][offset];
			}
			else if(Opens[0][offset] > Closes[0][offset]) ///short
			{
				return Highs[0][offset]-Opens[0][offset];
			}
			else return 0;
		}
		
		public double LowerWick(int offset)
		{
			if(Opens[0][offset] < Closes[0][offset]) ///long
			{
				return Opens[0][offset]-Lows[0][offset];
			}
			else if(Opens[0][offset] > Closes[0][offset]) ///short
			{
				return Closes[0][offset]-Lows[0][offset];
			}
			else return 0;
		}
		
		public double AbsBody(int offset)
		{
		
			return Math.Abs(Opens[0][offset] - Closes[0][offset]);
		}
		public double Body(int offset)
		{
			if(Opens[0][offset] < Closes[0][offset]) ///long
			{
				return Closes[0][offset]-Opens[0][offset];
			}
			else if(Opens[0][offset] > Closes[0][offset]) ///short
			{
				return Opens[0][offset]-Closes[0][offset];
			}
			else return 0;
		}
				#region supportingFunctions
		/// 
		
		
		

	

		
		public int offsetBarTrendDirection(int barIndex,int offset)
		{
			if(Closes[barIndex][offset] > Opens[barIndex][offset])
			{
				return 1;
			}
			else if(Closes[barIndex][offset] < Opens[barIndex][offset])
			{
				return -1;
			}
			else return 0;
		}
		#endregion
	}
	
	
}
