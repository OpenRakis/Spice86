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
 * Digital/analogue filter coefficient storage space organising the
 * storage as PoleZeroPairs so that we have as always a 2nd order filter
 *
 */
public class LayoutBase {

    private int m_numPoles;
    private PoleZeroPair[] m_pair;
    private double m_normalW;
    private double m_normalGain;

    public LayoutBase(PoleZeroPair[] pairs) {
        m_numPoles = pairs.Length * 2;
        m_pair = pairs;
    }

    public LayoutBase(int numPoles) {
        m_numPoles = 0;
        if (numPoles % 2 == 1) {
            m_pair = new PoleZeroPair[numPoles / 2 + 1];
        } else {
            m_pair = new PoleZeroPair[numPoles / 2];
        }
    }

    public void reset() {
        m_numPoles = 0;
    }

    public int getNumPoles() {
        return m_numPoles;
    }

    public void add(Complex pole, Complex zero) {
        m_pair[m_numPoles / 2] = new PoleZeroPair(pole, zero);
        ++m_numPoles;
    }

    public void addPoleZeroConjugatePairs(Complex pole, Complex zero) {
        m_pair[m_numPoles / 2] = new PoleZeroPair(pole, zero, Complex.Conjugate(pole),
                Complex.Conjugate(zero));
        m_numPoles += 2;
    }

    public void add(ComplexPair poles, ComplexPair zeros) {
        m_pair[m_numPoles / 2] = new PoleZeroPair(poles.first, zeros.first,
                poles.second, zeros.second);
        m_numPoles += 2;
    }

    public PoleZeroPair getPair(int pairIndex) {
        return m_pair[pairIndex];
    }

    public double getNormalW() {
        return m_normalW;
    }

    public double getNormalGain() {
        return m_normalGain;
    }

    public void setNormal(double w, double g) {
        m_normalW = w;
        m_normalGain = g;
    }
};
