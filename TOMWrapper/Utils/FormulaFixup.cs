﻿using Antlr4.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TabularEditor.TextServices;
using TabularEditor.UndoFramework;

namespace TabularEditor.TOMWrapper.Utils
{
    public static class FormulaFixup
    {
        /// <summary>
        /// Changes all references to object "obj", to reflect "newName"
        /// </summary>
        /// <param name="obj"></param>
        public static void DoFixup(IDaxObject obj)
        {
            foreach (var d in obj.ReferencedBy.ToList())
            {
                d.DependsOn.UpdateRef(obj);
            }
        }

        private static TabularModelHandler Handler { get { return TabularModelHandler.Singleton; } }
        private static Model Model { get { return TabularModelHandler.Singleton.Model; } }

        public static void BuildDependencyTree(IDaxDependantObject expressionObj)
        {
            foreach (var d in expressionObj.DependsOn.Keys) d.ReferencedBy.Remove(expressionObj);
            expressionObj.DependsOn.Clear();

            foreach (var prop in expressionObj.GetDAXProperties())
            {
                var tokens = new DAXLexer(new AntlrInputStream(expressionObj.GetDAX(prop) ?? "")).GetAllTokens();

                IToken lastTableRef = null;

                for (var i = 0; i < tokens.Count; i++)
                {
                    // TODO: This parsing could be used to check for invalid object references, for example to use in syntax highlighting or validation of expressions

                    var tok = tokens[i];
                    switch (tok.Type)
                    {
                        case DAXLexer.TABLE:
                        case DAXLexer.TABLE_OR_VARIABLE:
                            if (lastTableRef != null)
                            {
                                if (Model.Tables.Contains(lastTableRef.Text.NoQ(true))) expressionObj.AddDep(Model.Tables[lastTableRef.Text.NoQ(true)], prop, lastTableRef.StartIndex, lastTableRef.StopIndex, true);
                            }
                            lastTableRef = tok;
                            break;
                        case DAXLexer.COLUMN_OR_MEASURE:
                            var tableName = lastTableRef?.Text.NoQ(true);

                            // Referencing a table just before the object reference
                            if (tableName != null && Model.Tables.Contains(tableName))
                            {
                                var table = Model.Tables[tableName];
                                // Referencing a column on a specific table
                                if (table.Columns.Contains(tok.Text.NoQ()))
                                    expressionObj.AddDep(table.Columns[tok.Text.NoQ()], prop, tok.StartIndex, tok.StopIndex, false);
                                // Referencing a measure on a specific table
                                else if (table.Measures.Contains(tok.Text.NoQ()))
                                    expressionObj.AddDep(table.Measures[tok.Text.NoQ()], prop, tok.StartIndex, tok.StopIndex, false);
                            }
                            // No table reference before the object reference
                            else
                            {
                                var table = (expressionObj as ITabularTableObject)?.Table;
                                // Referencing a column without specifying a table (assume column in same table):
                                if (table != null && table.Columns.Contains(tok.Text.NoQ()))
                                {
                                    expressionObj.AddDep(table.Columns[tok.Text.NoQ()], prop, tok.StartIndex, tok.StopIndex, false);
                                }
                                // Referencing a measure or column without specifying a table
                                else
                                {
                                    Measure m = null;
                                    if (table != null && table.Measures.Contains(tok.Text.NoQ())) m = table.Measures[tok.Text.NoQ()];
                                    else
                                        m = Model.Tables.FirstOrDefault(t => t.Measures.Contains(tok.Text.NoQ()))?.Measures[tok.Text.NoQ()];

                                    if (m != null)
                                        expressionObj.AddDep(m, prop, tok.StartIndex, tok.StopIndex, false);
                                }
                            }
                            lastTableRef = null;
                            break;
                        default:
                            if (lastTableRef != null)
                            {
                                if (Model.Tables.Contains(lastTableRef.Text.NoQ(true))) expressionObj.AddDep(Model.Tables[lastTableRef.Text.NoQ(true)], prop, lastTableRef.StartIndex, lastTableRef.StopIndex, true);
                                lastTableRef = null;
                            }
                            break;
                    }
                    if (lastTableRef != null)
                    {
                        if (Model.Tables.Contains(lastTableRef.Text.NoQ(true))) expressionObj.AddDep(Model.Tables[lastTableRef.Text.NoQ(true)], prop, lastTableRef.StartIndex, lastTableRef.StopIndex, true);
                        //lastTableRef = null;
                    }
                }
            }
        }

        public static void BuildDependencyTree()
        {
            if (Handler.InsideTransaction) { Handler.EoB_BuildDependencyTree = true; return; }

            var sw = new Stopwatch();
            sw.Start();

            foreach (var eo in Model.Tables.SelectMany(t => t.GetChildren()).Concat(Model.Tables).OfType<IDaxDependantObject>())
            {
                BuildDependencyTree(eo);
            }

            sw.Stop();

            Console.WriteLine("Dependency tree built in {0} ms", sw.ElapsedMilliseconds);
        }
    }
}
