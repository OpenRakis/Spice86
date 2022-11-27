/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */


using System.Numerics;

namespace Spice86.Core.Backend.Audio.Iir;

/**
 * 
 * The mother of all filters. It contains the coefficients of all
 * filter stages as a sequence of 2nd order filters and the states
 * of the 2nd order filters which also imply if it's direct form I or II
 *
 */
public class Cascade {

    // coefficients
    private Biquad[] m_biquads;

    // the states of the filters
    private DirectFormAbstract[] m_states;

    // number of biquads in the system
    private int m_numBiquads;

    private int numPoles;

    public int getNumBiquads() {
        return m_numBiquads;
    }

    public Biquad getBiquad(int index) {
        return m_biquads[index];
    }

    public Cascade() {
        m_numBiquads = 0;
        m_biquads = Array.Empty<Biquad>();
        m_states = Array.Empty<DirectFormAbstract>();
    }

    public void reset() {
        for (int i = 0; i < m_numBiquads; i++)
            m_states[i].reset();
    }

    public double filter(double x) {
        double res = x;
        for (int i = 0; i < m_numBiquads; i++) {
            if (m_states[i] != null) {
                res = m_states[i].process1(res, m_biquads[i]);
            }
        }
        return res;
    }

    public Complex response(double normalizedFrequency) {
        double w = 2 * Math.PI * normalizedFrequency;
        Complex czn1 = ComplexUtils.polar2Complex(1.0, -w);
        Complex czn2 = ComplexUtils.polar2Complex(1.0, -2 * w);
        var ch = new Complex(1, 0);
        var cbot = new Complex(1, 0);

        for (int i = 0; i < m_numBiquads; i++) {
            Biquad stage = m_biquads[i];
            var cb = new Complex(1, 0);
            var ct = new Complex(stage.getB0() / stage.getA0(), 0);
            ct = MathSupplement.addmul(ct, stage.getB1() / stage.getA0(), czn1);
            ct = MathSupplement.addmul(ct, stage.getB2() / stage.getA0(), czn2);
            cb = MathSupplement.addmul(cb, stage.getA1() / stage.getA0(), czn1);
            cb = MathSupplement.addmul(cb, stage.getA2() / stage.getA0(), czn2);
            ch = Complex.Multiply(ch, ct);
            cbot = Complex.Multiply(cbot, cb);
        }

        return Complex.Divide(ch, cbot);
    }

    public void applyScale(double scale) {
        // For higher order filters it might be helpful
        // to spread this factor between all the stages.
        if (m_biquads.Length > 0) {
            m_biquads[0].applyScale(scale);
        }
    }

    private void createStates(int filterTypes) {
        switch (filterTypes) {
            case DirectFormAbstract.DIRECT_FORM_I:
                m_states = new DirectFormI[m_numBiquads];
                for (int i = 0; i < m_numBiquads; i++) {
                    m_states[i] = new DirectFormI();
                }
                break;
            case DirectFormAbstract.DIRECT_FORM_II:
            default:
                m_states = new DirectFormII[m_numBiquads];
                for (int i = 0; i < m_numBiquads; i++) {
                    m_states[i] = new DirectFormII();
                }
                break;
        }
    }

    public void setLayout(LayoutBase proto, int filterTypes) {
        numPoles = proto.getNumPoles();
        m_numBiquads = (numPoles + 1) / 2;
        m_biquads = new Biquad[m_numBiquads];
        createStates(filterTypes);
        for (int i = 0; i < m_numBiquads; ++i) {
            PoleZeroPair p = proto.getPair(i);
            m_biquads[i] = new Biquad();
            m_biquads[i].setPoleZeroPair(p);
        }
        applyScale(proto.getNormalGain()
                / Complex.Abs(response(proto.getNormalW() / (2 * Math.PI))));
    }

    public void setSOScoeff(double[][] sosCoefficients,
                int stateTypes) {
        m_numBiquads = sosCoefficients.Length;
        m_biquads = new Biquad[m_numBiquads];
        createStates(stateTypes);
        for (int i = 0; i < m_numBiquads; ++i) {
            m_biquads[i] = new Biquad();
            m_biquads[i].setCoefficients(
                sosCoefficients[i][3],
                sosCoefficients[i][4],
                sosCoefficients[i][5],
                sosCoefficients[i][0],
                sosCoefficients[i][1],
                sosCoefficients[i][2]
            );
        }
        applyScale(1);
    }


};
