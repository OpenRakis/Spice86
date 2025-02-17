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
    private Biquad[] _biquads;

    // the states of the filters
    private DirectFormAbstract[] _states;

    // number of biquads in the system
    private int _numBiquads;

    private int _numPoles;

    public int GetNumBiquads() {
        return _numBiquads;
    }

    public Biquad GetBiquad(int index) {
        return _biquads[index];
    }

    public Cascade() {
        _numBiquads = 0;
        _biquads = Array.Empty<Biquad>();
        _states = Array.Empty<DirectFormAbstract>();
    }

    public void Reset() {
        for (int i = 0; i < _numBiquads; i++)
            _states[i].Reset();
    }

    public double Filter(double x) {
        double res = x;
        for (int i = 0; i < _numBiquads; i++) {
            if (_states[i] != null) {
                res = _states[i].Process1(res, _biquads[i]);
            }
        }
        return res;
    }

    public Complex Response(double normalizedFrequency) {
        double w = 2 * Math.PI * normalizedFrequency;
        Complex czn1 = ComplexUtils.PolarToComplex(1.0, -w);
        Complex czn2 = ComplexUtils.PolarToComplex(1.0, -2 * w);
        var ch = new Complex(1, 0);
        var cbot = new Complex(1, 0);

        for (int i = 0; i < _numBiquads; i++) {
            Biquad stage = _biquads[i];
            var cb = new Complex(1, 0);
            var ct = new Complex(stage.GetB0 / stage.GetA0, 0);
            ct = MathSupplement.AddMul(ct, stage.GetB1 / stage.GetA0, czn1);
            ct = MathSupplement.AddMul(ct, stage.GetB2 / stage.GetA0, czn2);
            cb = MathSupplement.AddMul(cb, stage.GetA1 / stage.GetA0, czn1);
            cb = MathSupplement.AddMul(cb, stage.GetA2 / stage.GetA0, czn2);
            ch = Complex.Multiply(ch, ct);
            cbot = Complex.Multiply(cbot, cb);
        }

        return Complex.Divide(ch, cbot);
    }

    public void ApplyScale(double scale) {
        // For higher order filters it might be helpful
        // to spread this factor between all the stages.
        if (_biquads.Length > 0) {
            _biquads[0].ApplyScale(scale);
        }
    }

    private void CreateStates(int filterTypes) {
        switch (filterTypes) {
            case DirectFormAbstract.DirectFormI:
                _states = new DirectFormI[_numBiquads];
                for (int i = 0; i < _numBiquads; i++) {
                    _states[i] = new DirectFormI();
                }
                break;
            case DirectFormAbstract.DirectFormII:
            default:
                _states = new DirectFormII[_numBiquads];
                for (int i = 0; i < _numBiquads; i++) {
                    _states[i] = new DirectFormII();
                }
                break;
        }
    }

    public void SetLayout(LayoutBase proto, int filterTypes) {
        _numPoles = proto.NumPoles;
        _numBiquads = (_numPoles + 1) / 2;
        _biquads = new Biquad[_numBiquads];
        CreateStates(filterTypes);
        for (int i = 0; i < _numBiquads; ++i) {
            PoleZeroPair p = proto.GetPair(i);
            _biquads[i] = new Biquad();
            _biquads[i].SetPoleZeroPair(p);
        }
        ApplyScale(proto.NormalGain
                / Complex.Abs(Response(proto.NormalW / (2 * Math.PI))));
    }

    public void SetSOScoeff(double[][] sosCoefficients, int stateTypes) {
        _numBiquads = sosCoefficients.Length;
        _biquads = new Biquad[_numBiquads];
        CreateStates(stateTypes);
        for (int i = 0; i < _numBiquads; ++i) {
            _biquads[i] = new Biquad();
            _biquads[i].SetCoefficients(
                sosCoefficients[i][3],
                sosCoefficients[i][4],
                sosCoefficients[i][5],
                sosCoefficients[i][0],
                sosCoefficients[i][1],
                sosCoefficients[i][2]
            );
        }
        ApplyScale(1);
    }
};