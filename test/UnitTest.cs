﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using adb;
using System.Collections.Generic;


namespace test
{
    public class PlanCompare {
        static public void AreEqual(string l, string r) {
            char[] splitters = {' ', '\t', '\r', '\n'};
            var lw = l.Split(splitters, StringSplitOptions.RemoveEmptyEntries);
            var rw = r.Split(splitters, StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(lw.Length, rw.Length);
            for (int i = 0; i < lw.Length; i++)
                Assert.AreEqual(lw[i], rw[i]);
        }
    }

    [TestClass]
    public class ParserTest
    {
        [TestInitialize]
        public void TestInitialize()
        {
        }

        [TestMethod]
        public void TestColExpr()
        {
            ColExpr col = new ColExpr(null, "a", "a1");
            Assert.AreEqual("a.a1[0]", col.ToString());
        }

        [TestMethod]
        public void TestSelectStmt()
        {
            string sql = "with cte1 as (select * from a), cte2 as (select * from b) select a1,a1+a2 from cte1 where a1<6 group by a1, a1+a2 " +
                                "union select b2, b3 from cte2 where b2 > 3 group by b1, b1+b2 " +
                                "order by 2, 1 desc";
            var stmt = RawParser.ParseSQLStatement(sql) as SelectStmt;
            Assert.AreEqual(2, stmt.ctes_.Count);
            Assert.AreEqual(2, stmt.cores_.Count);
            Assert.AreEqual(2, stmt.orders_.Count);
        }
    }

    [TestClass]
    public class OptimizerTest
    {
        private TestContext testContextInstance;

        string error_ = null;
        internal List<Row> ExecuteSQL(string sql) {
            try
            {
                error_ = null;

                var stmt = RawParser.ParseSQLStatement(sql).Bind(null);
                stmt.CreatePlan();
                stmt.Optimize();
                var result = new PhysicCollect(stmt.GetPhysicPlan());
                result.Exec(null);
                return result.rows_;
            }
            catch (Exception e) {
                error_ = e.Message;
                return null;
            }
        }

        internal Tuple<List<Row>, PhysicNode> ExecuteSQL2(string sql)
        {
            try
            {
                error_ = null;

                var stmt = RawParser.ParseSQLStatement(sql).Bind(null);
                stmt.CreatePlan();
                var phyplan = stmt.Optimize().DirectToPhysical();
                var result = new PhysicCollect(phyplan);
                result.Exec(null);
                return new Tuple<List<Row>, PhysicNode>(result.rows_, phyplan);
            }
            catch (Exception e)
            {
                error_ = e.Message;
                return null;
            }
        }

        /// <summary>
        ///  Gets or sets the test context which provides
        ///  information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get { return testContextInstance; }
            set { testContextInstance = value; }
        }

        [TestInitialize]
        public void TestInitialize()
        {
        }

        [TestMethod]
        public void TestExecCrossJoin()
        {
            var sql = "select a.a1 from a, b where a2 > 1";
            var result = ExecuteSQL(sql);
            Assert.AreEqual(2 * 3, result.Count);
            sql = "select a.a1 from a, b where a2>2";
            result = ExecuteSQL(sql);
            Assert.AreEqual(1 * 3, result.Count);
        }

        [TestMethod]
        public void TestExecSubFrom()
        {
            var sql = "select * from a, (select * from b) c";
            var result2 = ExecuteSQL2(sql);
            Assert.AreEqual(9, result2.Item1.Count);
            Assert.AreEqual("0,1,2,0,1,2", result2.Item1[0].ToString());
            Assert.AreEqual("2,3,4,2,3,4", result2.Item1[8].ToString());
            sql = "select * from a, (select * from b where b2>2) c";
            var result = ExecuteSQL(sql);
            Assert.AreEqual(3, result.Count);
            sql = "select b.a1 + b.a2 from (select a1 from a) b";
            result = ExecuteSQL(sql);
            Assert.IsNull(result);
            Assert.IsTrue(error_.Contains("SemanticAnalyzeException"));
            sql = "select b.a1 + a2 from (select a1,a2 from a) b";
            result = ExecuteSQL(sql);
            Assert.AreEqual(3, result.Count);
            sql = "select a1 from (select a1,a3 from a) b";
            result = ExecuteSQL(sql);
            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void TestExecSubquery()
        {
            var sql = "select a1, a3  from a where a.a1 = (select b1,b2 from b)";
            var result = ExecuteSQL(sql); Assert.IsNull(result);
            Assert.IsTrue(error_.Contains("SemanticAnalyzeException"));
            sql = "select a1, a2  from a where a.a1 = (select b1 from b)";
            result = ExecuteSQL(sql); Assert.IsNull(result);
            Assert.IsTrue(error_.Contains("SemanticExecutionException"));

            sql = "select a1, a3  from a where a.a1 = (select b1 from b where b2 = 3)";
            result = ExecuteSQL(sql);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual ("2,4", result[0].ToString());
            sql = "select a1, a3  from a where a.a1 = (select b1 from b where b2 = 4)";
            result = ExecuteSQL(sql);
            Assert.AreEqual(0, result.Count);

            sql = "select a1, a3  from a where a.a1 = (select b1 from b where b2 = a2 and b3<3)";
            result = ExecuteSQL(sql);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("0,2", result[0].ToString());
        }

        [TestMethod]
        public void TestExecSelectFilter()
        {
            var result = ExecuteSQL("select * from a");
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(3, result[1].values_.Count);
            Assert.AreEqual(1, result[0].values_[1]);
            Assert.AreEqual(2, result[1].values_[1]);
            Assert.AreEqual(3, result[2].values_[1]);
            result = ExecuteSQL("select a1+a2,a1-a2,a1*a2 from a");
            Assert.AreEqual(3, result.Count);
            result = ExecuteSQL("select a1 from a where a2>1");
            Assert.AreEqual(2, result.Count);
            result = ExecuteSQL("select a.a1 from a where a2 > 1 and a3> 3");
            Assert.AreEqual(1, result.Count);
            result = ExecuteSQL("select a1 from a where a2>2");
            Assert.AreEqual(1, result.Count);
            result = ExecuteSQL("select a1,a1,a3,a3 from a where a1>1");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("2,2,4,4", result[0].ToString());
        }

        [TestMethod]
        public void TestExecResult() {
            string sql = "select 2+6*3+2*6";
            var result = ExecuteSQL(sql); 
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(32, result[0].values_[0]);
        }

        [TestMethod]
        public void TestExecProject()
        {
            string sql = "select b.a1 + a2 from (select a1,a2 from a, c) b";
            var result = ExecuteSQL(sql);
            Assert.AreEqual(9, result.Count);
            int i; Assert.AreEqual(1, result[0].values_.Count);
            for (i = 0; i < 3; i++) Assert.AreEqual(1, result[i].values_[0]);
            for (; i < 6; i++) Assert.AreEqual(3, result[i].values_[0]);
            for (; i < 9; i++) Assert.AreEqual(5, result[i].values_[0]);
        }

        [TestMethod]
        public void TestPushdown()
        {
            string sql = "select a.a1,a.a1+a.a2 from a where a.a2 > 3";
            var stmt = RawParser.ParseSQLStatement(sql).Bind(null);
            stmt.CreatePlan();
            var plan = stmt.Optimize();
            var answer = @"LogicGet a
                                Output: a.a1[0],a.a1[0]+a.a2[1]
                                Filter: a.a2[1]>3";
            PlanCompare.AreEqual (answer,  plan.PrintString(0));

            sql = "select 1 from a where a.a1 > (select b1 from b where b.b2 > (select c2 from c where c.c2=b2) and b.b1 > ((select c2 from c where c.c2=b2)))";
            stmt = RawParser.ParseSQLStatement(sql).Bind(null);
            stmt.CreatePlan();
            stmt.Optimize();
            var phyplan = stmt.GetPhysicPlan();
            answer = @"PhysicGet a
                        Output: 1
                        Filter: a.a1[0]>@0
                        <SubLink> 0
                        -> PhysicFilter
                            Output: b.b1[0]
                            Filter: b.b2[1]>@1 and b.b1[0]>@2
                            <SubLink> 1
                            -> PhysicFilter
                                Output: c.c2[0]
                                Filter: c.c2[1]=?b.b2[1]
                              -> PhysicGet c
                                  Output: c.c2[1],c.c2[1]
                            <SubLink> 2
                            -> PhysicFilter
                                Output: c.c2[0]
                                Filter: c.c2[1]=?b.b2[1]
                              -> PhysicGet c
                                  Output: c.c2[1],c.c2[1]
                          -> PhysicGet b
                              Output: b.b1[0],b.b2[1],b.b1[0]";
            PlanCompare.AreEqual(answer, phyplan.PrintString(0));
        }
    }
}