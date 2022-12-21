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
    private int _numPoles;
    private readonly PoleZeroPair[] _pair;
    private double _normalW;
    private double _normalGain;

    public LayoutBase(PoleZeroPair[] pairs) {
        _numPoles = pairs.Length * 2;
        _pair = pairs;
    }

    public LayoutBase(int numPoles) {
        _numPoles = 0;
        if (numPoles % 2 == 1) {
            _pair = new PoleZeroPair[numPoles / 2 + 1];
        } else {
            _pair = new PoleZeroPair[numPoles / 2];
        }
    }

    public void Reset() {
        _numPoles = 0;
    }

    public int NumPoles  => _numPoles;

    public void Add(Complex pole, Complex zero) {
        _pair[_numPoles / 2] = new PoleZeroPair(pole, zero);
        ++_numPoles;
    }

    public void AddPoleZeroConjugatePairs(Complex pole, Complex zero) {
        _pair[_numPoles / 2] = new PoleZeroPair(pole, zero, Complex.Conjugate(pole),
                Complex.Conjugate(zero));
        _numPoles += 2;
    }

    public void Add(ComplexPair poles, ComplexPair zeros) {
        _pair[_numPoles / 2] = new PoleZeroPair(poles.First, zeros.First,
                poles.Second, zeros.Second);
        _numPoles += 2;
    }

    public PoleZeroPair GetPair(int pairIndex) {
        return _pair[pairIndex];
    }

    public double NormalW => _normalW;

    public double NormalGain => _normalGain;

    public void SetNormal(double w, double g) {
        _normalW = w;
        _normalGain = g;
    }
};
